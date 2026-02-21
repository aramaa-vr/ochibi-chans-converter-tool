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

        internal static bool AdjustByMergeArmatureMapping(List<Transform> costumeRoots, List<string> logs)
        {
            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            logs?.Add(L("Log.CostumeScaleHeader"));
            logs?.Add(OCTLocalization.Format("Log.CostumeCount", costumeRoots.Count));

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
            }

            logs?.Add(OCTLocalization.Format("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }
#else
        private static void SyncOneCostume(Transform costumeRoot, List<string> logs)
        {
            _ = costumeRoot;
            _ = logs;
        }
#endif
    }
}
#endif
