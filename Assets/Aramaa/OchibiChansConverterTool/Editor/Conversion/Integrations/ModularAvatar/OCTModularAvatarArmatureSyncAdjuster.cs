#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// ModularAvatarMergeArmature のボーン対応表を利用し、衣装側ボーンの localScale を補正する。
    /// </summary>
    internal static class OCTModularAvatarArmatureSyncAdjuster
    {
        private const float ScaleEpsilon = 0.0001f;
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
                AdjustOneCostume(costumeRoot, logs);
            }

            return true;
        }

        private sealed class MergeArmatureBoneScaleMapping
        {
            public string BaseBoneName;
            public Vector3 BaseScale;
            public Transform OutfitBone;
        }

#if CHIBI_MODULAR_AVATAR
        private static void AdjustOneCostume(Transform costumeRoot, List<string> logs)
        {
            if (costumeRoot == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));

            var mergeArmatureMappings = BuildMergeArmatureMappings(costumeRoot, logs);
            if (mergeArmatureMappings.Count == 0)
            {
                logs?.Add(F("Log.CostumeApplied", costumeRoot.name, 0));
                return;
            }

            var appliedCount = 0;
            var costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();

            ApplyMergeArmatureScaleMappings(mergeArmatureMappings, costumeBones, logs, costumeRoot, ref appliedCount);

            logs?.Add(F("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }

        private static List<MergeArmatureBoneScaleMapping> BuildMergeArmatureMappings(Transform costumeRoot, List<string> logs)
        {
            var result = new List<MergeArmatureBoneScaleMapping>();

            var mergeArmature = costumeRoot.GetComponentInParent<ModularAvatarMergeArmature>(true);
            if (mergeArmature == null)
            {
                logs?.Add($"[CostumeScale] skip: merge armature not found for '{costumeRoot.name}'");
                return result;
            }

            var mapping = mergeArmature.GetBonesMapping();
            if (mapping == null || mapping.Count == 0)
            {
                logs?.Add($"[CostumeScale] skip: bone mapping empty for '{costumeRoot.name}'");
                return result;
            }

            foreach (var pair in mapping)
            {
                var baseBone = pair.Item1;
                var outfitBone = pair.Item2;
                if (baseBone == null || outfitBone == null || !outfitBone.IsChildOf(costumeRoot))
                {
                    continue;
                }

                if (IsNearlyOne(baseBone.localScale))
                {
                    continue;
                }

                result.Add(new MergeArmatureBoneScaleMapping
                {
                    BaseBoneName = baseBone.name,
                    BaseScale = baseBone.localScale,
                    OutfitBone = outfitBone
                });
            }

            return result;
        }
#else
        private static void AdjustOneCostume(Transform costumeRoot, List<string> logs)
        {
            _ = costumeRoot;
            _ = logs;
        }
#endif

        private static bool IsNearlyOne(Vector3 s)
        {
            return Mathf.Abs(s.x - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.y - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.z - 1f) < ScaleEpsilon;
        }

        private static void ApplyMergeArmatureScaleMappings(
            List<MergeArmatureBoneScaleMapping> mergeArmatureMappings,
            List<Transform> costumeBones,
            List<string> logs,
            Transform costumeRoot,
            ref int appliedCount
        )
        {
            foreach (var mapping in mergeArmatureMappings)
            {
                if (mapping?.OutfitBone == null || IsNearlyOne(mapping.BaseScale))
                {
                    continue;
                }

                if (!costumeBones.Contains(mapping.OutfitBone))
                {
                    continue;
                }

                TryApplyScaleToBone(
                    mapping.OutfitBone,
                    mapping.BaseScale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    mapping.BaseBoneName,
                    L("Log.MatchArmature"),
                    ref appliedCount
                );
            }
        }

        private static bool TryApplyScaleToBone(
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
