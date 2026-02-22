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
    /// Modular Avatar Merge Armature のボーン対応を利用して、衣装側ボーンの localScale を補正します。
    /// </summary>
    internal static class OCTModularAvatarCostumeScaleAdjuster
    {
        private const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);

        internal static int AdjustByMergeArmatureMapping(GameObject dstRoot, List<string> logs = null)
        {
            if (dstRoot == null)
            {
                return 0;
            }

#if CHIBI_MODULAR_AVATAR
            int appliedCount = 0;
            var mergers = dstRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true);

            foreach (var merger in mergers)
            {
                if (merger == null)
                {
                    continue;
                }

                var mappings = merger.GetBonesMapping();
                if (mappings == null || mappings.Count == 0)
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(merger.gameObject, L("Undo.AdjustCostumeScales"));

                foreach (var pair in mappings)
                {
                    var baseBone = pair.Item1;
                    var mergeBone = pair.Item2;
                    if (baseBone == null || mergeBone == null)
                    {
                        continue;
                    }

                    if (IsNearlyOne(baseBone.localScale))
                    {
                        continue;
                    }

                    var before = mergeBone.localScale;
                    var adjusted = Vector3.Scale(before, baseBone.localScale);
                    if (IsNearlyEqual(before, adjusted))
                    {
                        continue;
                    }

                    mergeBone.localScale = adjusted;
                    EditorUtility.SetDirty(mergeBone);
                    appliedCount++;

                    new OCTConversionLogger(logs).Add(
                        "Log.CostumeScaleApplied",
                        merger.name,
                        $"{baseBone.name}->{mergeBone.name}",
                        "ModularAvatarMergeArmature",
                        mergeBone.name
                    );
                }
            }

            if (appliedCount > 0)
            {
                logs?.Add($"[MA Merge Armature] Applied scale adjustments: {appliedCount}");
            }

            return appliedCount;
#else
            return 0;
#endif
        }

        private static bool IsNearlyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.y - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.z - 1f) < ScaleEpsilon;
        }

        private static bool IsNearlyEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < ScaleEpsilon
                   && Mathf.Abs(a.y - b.y) < ScaleEpsilon
                   && Mathf.Abs(a.z - b.z) < ScaleEpsilon;
        }
    }
}
#endif
