#if UNITY_EDITOR
using System.Collections.Generic;
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
        private static string L(string key) => OCTLocalization.Get(key);

        internal static bool AdjustByMergeArmatureMapping(List<Transform> costumeRoots, List<string> logs)
        {
            return OCTCostumeScaleApplyUtility.AdjustCostumeRoots(costumeRoots, logs, AdjustOneCostume);
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
            if (!OCTCostumeScaleApplyUtility.TryPrepareCostume(
                    costumeRoot,
                    L("Undo.AdjustCostumeScales"),
                    out var costumeBones
                ))
            {
                return;
            }

            var mergeArmatureMappings = BuildMergeArmatureMappings(costumeRoot, logs);
            if (mergeArmatureMappings.Count == 0)
            {
                OCTCostumeScaleApplyUtility.LogCostumeApplied(logs, costumeRoot, 0);
                return;
            }

            var appliedCount = 0;

            ApplyMergeArmatureScaleMappings(mergeArmatureMappings, costumeBones, logs, costumeRoot, ref appliedCount);

            OCTCostumeScaleApplyUtility.LogCostumeApplied(logs, costumeRoot, appliedCount);
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
                if (baseBone == null || outfitBone == null)
                {
                    continue;
                }

                if (OCTCostumeScaleApplyUtility.IsNearlyOne(baseBone.localScale))
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
                if (mapping?.OutfitBone == null || OCTCostumeScaleApplyUtility.IsNearlyOne(mapping.BaseScale))
                {
                    continue;
                }

                // MeshSettings の transform とボーン階層が別枝のケースを許容するため、
                // 収集時には IsChildOf(costumeRoot) で除外せず、最終適用時に costumeBones で安全に絞り込む。
                if (!costumeBones.Contains(mapping.OutfitBone))
                {
                    continue;
                }

                OCTCostumeScaleApplyUtility.TryApplyScaleToBone(
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

    }
}
#endif
