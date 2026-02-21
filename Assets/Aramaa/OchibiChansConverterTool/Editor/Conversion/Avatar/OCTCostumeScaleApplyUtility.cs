#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 衣装ボーンへの scale 適用・ログ記録の共通処理。
    /// </summary>
    internal static class OCTCostumeScaleApplyUtility
    {
        private const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static bool AdjustCostumeRoots(
            List<Transform> costumeRoots,
            List<string> logs,
            Action<Transform, List<string>> adjustOneCostume
        )
        {
            new OCTConversionLogger(logs).Add("Log.CostumeScaleCriteria");

            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            logs?.Add(L("Log.CostumeScaleHeader"));
            logs?.Add(F("Log.CostumeCount", costumeRoots.Count));

            foreach (var costumeRoot in costumeRoots)
            {
                adjustOneCostume?.Invoke(costumeRoot, logs);
            }

            return true;
        }

        internal static bool TryPrepareCostume(
            Transform costumeRoot,
            string undoLabel,
            out List<Transform> costumeBones
        )
        {
            costumeBones = null;
            if (costumeRoot == null)
            {
                return false;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, undoLabel);
            costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();
            return true;
        }

        internal static void LogCostumeApplied(List<string> logs, Transform costumeRoot, int appliedCount)
        {
            logs?.Add(F("Log.CostumeApplied", costumeRoot?.name ?? L("Log.NullValue"), appliedCount));
        }

        internal static bool IsNearlyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < ScaleEpsilon &&
                   Mathf.Abs(scale.y - 1f) < ScaleEpsilon &&
                   Mathf.Abs(scale.z - 1f) < ScaleEpsilon;
        }

        internal static bool TryApplyScaleToBone(
            Transform bone,
            Vector3 scaleModifier,
            List<Transform> removalTarget,
            List<string> logs,
            Transform costumeRoot,
            string modifierKey,
            string matchLabel,
            ref int appliedCount
        )
        {
            if (bone == null)
            {
                return false;
            }

            bone.localScale = Vector3.Scale(bone.localScale, scaleModifier);
            EditorUtility.SetDirty(bone);
            appliedCount++;

            new OCTConversionLogger(logs).Add(
                "Log.CostumeScaleApplied",
                costumeRoot?.name ?? L("Log.NullValue"),
                modifierKey,
                matchLabel,
                FormatMatchedBoneForLog(bone, costumeRoot));

            removalTarget?.Remove(bone);
            return true;
        }

        private static string FormatMatchedBoneForLog(Transform bone, Transform costumeRoot)
        {
            if (bone == null)
            {
                return L("Log.NullValue");
            }

            var path = GetTransformPath(bone, costumeRoot);
            return $"{bone.name} ({path})";
        }

        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == null)
            {
                return L("Log.NullValue");
            }

            if (root == null)
            {
                return target.name;
            }

            var rel = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(rel) ? root.name : root.name + "/" + rel;
        }
    }
}
#endif
