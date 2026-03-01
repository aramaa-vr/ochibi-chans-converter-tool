#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - MA MergeArmature のボーン対応情報を使って衣装ボーン localScale を補正します。
// - MA 未導入や取得失敗時は何も変更せず終了します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 反射で GetBonesMapping を呼ぶため、MA 側 API 変更時はここが影響を受けます。
// - 1.0 に近いスケールは変更対象から除外し、差分最小化を優先します。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - Undo 登録は維持する（ユーザーが戻せることを保証）。
// - 補正条件の閾値変更時は既存アバターで目視確認を必須にする。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA マッピングに基づく衣装スケール補正クラスです。
    /// </summary>
    internal static class OCTModularAvatarCostumeScaleAdjuster
    {
        private const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);

        /// <summary>
        /// MergeArmature マッピングを使って衣装スケールを補正し、適用数を返します。
        /// </summary>
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

                if (mappingsObj is ICollection col && col.Count == 0)
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(merger.gameObject, L("Undo.AdjustCostumeScales"));

                foreach (var pair in mappingsEnumerable)
                {
                    if (pair == null) continue;

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

        /// <summary>
        /// スケールが (1,1,1) に十分近いかを判定します。
        /// </summary>
        private static bool IsNearlyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.y - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.z - 1f) < ScaleEpsilon;
        }

        /// <summary>
        /// 2 つのスケールが十分近いかを判定します。
        /// </summary>
        private static bool IsNearlyEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < ScaleEpsilon
                   && Mathf.Abs(a.y - b.y) < ScaleEpsilon
                   && Mathf.Abs(a.z - b.z) < ScaleEpsilon;
        }
    }
}
#endif
