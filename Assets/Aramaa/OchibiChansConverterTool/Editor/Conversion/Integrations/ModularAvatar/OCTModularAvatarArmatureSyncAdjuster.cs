#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// ModularAvatarMergeArmature のボーン対応表を利用し、衣装側ボーンの Transform をベースへ同期する。
    /// </summary>
    internal static class OCTModularAvatarArmatureSyncAdjuster
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static bool AdjustByMergeArmatureMapping(List<Transform> costumeRoots, List<string> logs)
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
                SyncOneCostume(costumeRoot, logs);
            }

            return true;
        }

#if CHIBI_MODULAR_AVATAR
        private static void SyncOneCostume(Transform costumeRoot, List<string> logs)
        {
            if (costumeRoot == null)
            {
                return;
            }

            var mergeArmature = costumeRoot.GetComponentInParent<ModularAvatarMergeArmature>(true);
            if (mergeArmature == null)
            {
                logs?.Add($"[CostumeScale] skip: merge armature not found for '{costumeRoot.name}'");
                return;
            }

            var mapping = mergeArmature.GetBonesMapping();
            if (mapping == null || mapping.Count == 0)
            {
                logs?.Add($"[CostumeScale] skip: bone mapping empty for '{costumeRoot.name}'");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));

            var appliedCount = 0;
            foreach (var pair in mapping)
            {
                var baseBone = pair.Item1;
                var outfitBone = pair.Item2;

                if (baseBone == null || outfitBone == null || !outfitBone.IsChildOf(costumeRoot))
                {
                    continue;
                }

                outfitBone.localPosition = baseBone.localPosition;
                outfitBone.localRotation = baseBone.localRotation;
                outfitBone.localScale = baseBone.localScale;

                EditorUtility.SetDirty(outfitBone);
                appliedCount++;

                var baseRoot = mergeArmature.mergeTargetObject != null ? mergeArmature.mergeTargetObject.transform : null;
                var modifierKey = BuildModifierKeyForLog(baseBone, baseRoot);
                new OCTConversionLogger(logs).Add(
                    "Log.CostumeScaleApplied",
                    costumeRoot.name,
                    modifierKey,
                    L("Log.MatchArmature"),
                    FormatMatchedBoneForLog(outfitBone, costumeRoot));
            }

            logs?.Add(F("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }
#else
        private static void SyncOneCostume(Transform costumeRoot, List<string> logs)
        {
            _ = costumeRoot;
            _ = logs;
        }
#endif

        private static string BuildModifierKeyForLog(Transform baseBone, Transform baseRoot)
        {
            if (baseBone == null)
            {
                return L("Log.NullValue");
            }

            var relativePath = AnimationUtility.CalculateTransformPath(baseBone, baseRoot);
            if (string.IsNullOrEmpty(relativePath))
            {
                return baseBone.name;
            }

            return $"{baseBone.name} ({relativePath})";
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
