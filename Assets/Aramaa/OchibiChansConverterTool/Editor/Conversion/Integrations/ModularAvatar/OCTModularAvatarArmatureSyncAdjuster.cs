#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

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

        internal static bool HasMergeArmatureMapping(Transform costumeRoot)
        {
            return OCTModularAvatarMergeArmatureUtility.TryCollectBoneScaleMappings(
                costumeRoot,
                new List<OCTModularAvatarMergeArmatureUtility.BoneScaleMapping>()
            );
        }

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

            var mergeArmatureMappings = new List<OCTModularAvatarMergeArmatureUtility.BoneScaleMapping>();
            var hasMergeArmatureMappings = OCTModularAvatarMergeArmatureUtility.TryCollectBoneScaleMappings(costumeRoot, mergeArmatureMappings);
            if (!hasMergeArmatureMappings)
            {
                OCTCostumeScaleApplyUtility.LogCostumeApplied(logs, costumeRoot, 0);
                return;
            }

            var appliedCount = 0;

            ApplyMergeArmatureScaleMappings(mergeArmatureMappings, costumeBones, logs, costumeRoot, ref appliedCount);

            OCTCostumeScaleApplyUtility.LogCostumeApplied(logs, costumeRoot, appliedCount);
        }

        private static void ApplyMergeArmatureScaleMappings(
            List<OCTModularAvatarMergeArmatureUtility.BoneScaleMapping> mergeArmatureMappings,
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
                    L("Log.MatchModularAvatar"),
                    ref appliedCount
                );
            }
        }

    }
}
#endif
