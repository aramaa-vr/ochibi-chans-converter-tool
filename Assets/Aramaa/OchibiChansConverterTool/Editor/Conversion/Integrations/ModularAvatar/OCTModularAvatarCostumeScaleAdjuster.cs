#if UNITY_EDITOR
using System.Collections.Generic;
using System.Collections;
using UnityEditor;
using UnityEngine;

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

            if (!OCTModularAvatarReflection.TryGetMergeArmatureType(out var mergeArmatureType))
            {
                return 0;
            }

            int appliedCount = 0;

            var mergers = OCTModularAvatarReflection
                .GetComponentsInChildren(dstRoot, mergeArmatureType, includeInactive: true);

            foreach (var merger in mergers)
            {
                if (merger == null)
                {
                    continue;
                }

                var mappingsObj = OCTModularAvatarReflection.InvokeGetBonesMapping(merger);
                if (!(mappingsObj is IEnumerable mappingsEnumerable))
                {
                    continue;
                }

                // 空チェック（Count を持つ場合はそれを使う）
                if (mappingsObj is ICollection col && col.Count == 0)
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(merger.gameObject, L("Undo.AdjustCostumeScales"));

                foreach (var pair in mappingsEnumerable)
                {
                    if (pair == null) continue;

                    // List<(Transform, Transform)>（ValueTuple）想定
                    if (!OCTModularAvatarReflection.TryGetValueTupleItem(pair, "Item1", out var baseBone)) continue;
                    if (!OCTModularAvatarReflection.TryGetValueTupleItem(pair, "Item2", out var mergeBone)) continue;

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

                    var baseBonePath = OCTConversionLogFormatter.GetHierarchyPath(baseBone);
                    var mergeBonePath = OCTConversionLogFormatter.GetHierarchyPath(mergeBone);

                    new OCTConversionLogger(logs).Add(
                        "Log.CostumeScaleApplied",
                        merger.name,
                        $"{baseBonePath}->{mergeBonePath}",
                        mergeArmatureType.Name,
                        mergeBonePath
                    );
                }
            }

            if (appliedCount > 0)
            {
                logs?.Add($"[MA Merge Armature] Applied scale adjustments: {appliedCount}");
            }

            return appliedCount;
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
