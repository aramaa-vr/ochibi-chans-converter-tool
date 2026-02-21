#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 検出済みの衣装ルートに対し、ボーン localScale 補正のみを担当します。
// - BlendShape 同期は OCTCostumeBlendShapeAdjuster に分離されています。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - このクラスは「衣装の検出」は行いません。検出済み costumeRoots を入力として受け取ります。
// - 変換の互換性のため、マッチ順序は
//   完全一致 -> Armature相対パス一致 -> 部分一致 の順を維持します。
// - Undo とログ記録を残すことで、変換後の確認・巻き戻しをしやすくしています。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - マッチング順序・ログキーを変更する場合は既存アバターの変換結果への影響を確認すること。
// - MA 依存コード（コンポーネント検出など）は本クラスに入れず、呼び出し側で解決すること。
// - 補正対象の条件を増やす場合は、既存ロジックを壊さないよう段階的に追加すること。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 検出済みの衣装ルートに対して、ボーン localScale の補正を適用する責務を持つクラス。
    /// </summary>
    internal static class OCTCostumeScaleAdjuster
    {
        private const int MaxLoggedExcludedArmaturePaths = 1000;
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        /// <summary>
        /// 検出済み衣装ルート群に対し、スケール補正を順に適用します。
        /// </summary>
        internal static bool AdjustCostumeScales(
            GameObject dstRoot,
            GameObject basePrefabRoot,
            List<Transform> costumeRoots,
            List<string> logs
        )
        {
            if (dstRoot == null || basePrefabRoot == null)
            {
                return false;
            }

            var dstArmature = OCTEditorUtility.FindAvatarMainArmature(dstRoot.transform);
            if (dstArmature == null)
            {
                return false;
            }

            var baseArmaturePaths = BuildBaseArmatureTransformPaths(basePrefabRoot, logs);
            var avatarBoneScaleModifiers = BuildAvatarBoneScaleModifiers(dstArmature, baseArmaturePaths, logs);
            if (avatarBoneScaleModifiers.Count == 0)
            {
                return true;
            }

            return OCTCostumeScaleApplyUtility.AdjustCostumeRoots(
                costumeRoots,
                logs,
                (costumeRoot, sharedLogs) => AdjustOneCostume(costumeRoot, avatarBoneScaleModifiers, sharedLogs)
            );
        }

        private sealed class AvatarBoneScaleModifier
        {
            public string Name;
            public Vector3 Scale;
            public string RelativePath;
        }

        /// <summary>
        /// ベースPrefabのArmature配下から、比較用の安定パス集合を構築します。
        /// </summary>
        private static HashSet<string> BuildBaseArmatureTransformPaths(GameObject basePrefabRoot, List<string> logs)
        {
            if (basePrefabRoot == null)
            {
                return null;
            }

            var baseArmature = OCTEditorUtility.FindAvatarMainArmature(basePrefabRoot.transform);
            if (baseArmature == null)
            {
                logs?.Add(L("Log.BaseArmatureMissing"));
                return null;
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var transforms = baseArmature.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                paths.Add(OCTCostumeScaleApplyUtility.GetStableTransformPathWithSiblingIndex(t, baseArmature));
            }

            logs?.Add(F("Log.BaseArmaturePathCount", paths.Count));
            return paths;
        }

        /// <summary>
        /// 変換先アバター側の非等倍スケールボーンを抽出し、衣装へ適用する補正リストを作成します。
        /// </summary>
        private static List<AvatarBoneScaleModifier> BuildAvatarBoneScaleModifiers(
            Transform avatarArmature,
            HashSet<string> allowedArmaturePaths,
            List<string> logs
        )
        {
            var result = new List<AvatarBoneScaleModifier>();
            if (avatarArmature == null)
            {
                return result;
            }

            int excludedCount = 0;
            var excludedPaths = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            var bones = avatarArmature.GetComponentsInChildren<Transform>(true);
            foreach (var b in bones)
            {
                if (b == null || OCTCostumeScaleApplyUtility.IsNearlyOne(b.localScale))
                {
                    continue;
                }

                if (allowedArmaturePaths != null)
                {
                    var path = OCTCostumeScaleApplyUtility.GetStableTransformPathWithSiblingIndex(b, avatarArmature);
                    var readablePath = OCTCostumeScaleApplyUtility.GetTransformPath(b, avatarArmature);
                    if (!allowedArmaturePaths.Contains(path))
                    {
                        excludedCount++;
                        if (excludedPaths.Count < MaxLoggedExcludedArmaturePaths)
                        {
                            excludedPaths.Add(readablePath);
                        }

                        continue;
                    }
                }

                if (seenNames.Add(b.name))
                {
                    result.Add(new AvatarBoneScaleModifier
                    {
                        Name = b.name,
                        Scale = b.localScale,
                        RelativePath = AnimationUtility.CalculateTransformPath(b, avatarArmature)
                    });
                }
            }

            if (allowedArmaturePaths != null)
            {
                logs?.Add(F("Log.ArmatureExcludedCount", excludedCount));
                if (excludedPaths.Count > 0)
                {
                    logs?.Add(L("Log.ArmatureExcludedHeader"));
                    foreach (var p in excludedPaths)
                    {
                        logs?.Add(F("Log.PathEntry", p));
                    }

                    if (excludedCount > excludedPaths.Count)
                    {
                        logs?.Add(L("Log.PathEntryEllipsis"));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 1つの衣装ルート配下に対して、ボーンスケール補正を適用します。
        /// </summary>
        private static void AdjustOneCostume(
            Transform costumeRoot,
            List<AvatarBoneScaleModifier> avatarBoneScaleModifiers,
            List<string> logs
        )
        {
            if (avatarBoneScaleModifiers == null || avatarBoneScaleModifiers.Count == 0)
            {
                return;
            }

            if (!OCTCostumeScaleApplyUtility.TryPrepareCostume(
                    costumeRoot,
                    L("Undo.AdjustCostumeScales"),
                    out var costumeBones
                ))
            {
                return;
            }

            int appliedCount = 0;
            var costumeArmature = OCTEditorUtility.FindAvatarMainArmature(costumeRoot);

            ApplyScaleModifiers(
                avatarBoneScaleModifiers,
                costumeBones,
                costumeArmature,
                logs,
                costumeRoot,
                ref appliedCount
            );

            OCTCostumeScaleApplyUtility.LogCostumeApplied(logs, costumeRoot, appliedCount);
        }

        private static void ApplyScaleModifiers(
            List<AvatarBoneScaleModifier> avatarBoneScaleModifiers,
            List<Transform> costumeBones,
            Transform costumeArmature,
            List<string> logs,
            Transform costumeRoot,
            ref int appliedCount
        )
        {
            foreach (var modifier in avatarBoneScaleModifiers)
            {
                var temp = costumeBones;
                var modifierKeyForLog = OCTCostumeScaleApplyUtility.BuildModifierKeyForLog(modifier?.Name, modifier?.RelativePath);

                var matched = TryApplyScaleToFirstMatch(
                    temp,
                    bone => string.Equals(bone.name, modifier.Name, StringComparison.Ordinal),
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifierKeyForLog,
                    L("Log.MatchExact"),
                    ref appliedCount
                );

                if (matched)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(modifier.RelativePath) && costumeArmature != null)
                {
                    var normalizedPath = OCTEditorUtility.NormalizeRelPathFor(costumeArmature, modifier.RelativePath);
                    var candidate = string.IsNullOrEmpty(normalizedPath)
                        ? costumeArmature
                        : costumeArmature.Find(normalizedPath);
                    if (candidate != null && !costumeBones.Contains(candidate))
                    {
                        candidate = null;
                    }

                    if (OCTCostumeScaleApplyUtility.TryApplyScaleToBone(
                            candidate,
                            modifier.Scale,
                            costumeBones,
                            logs,
                            costumeRoot,
                            modifierKeyForLog,
                            L("Log.MatchArmature"),
                            ref appliedCount
                        ))
                    {
                        continue;
                    }
                }

                TryApplyScaleToFirstMatch(
                    temp,
                    bone => bone.name.Contains(modifier.Name),
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifierKeyForLog,
                    L("Log.MatchContains"),
                    ref appliedCount
                );
            }
        }

        /// <summary>
        /// 条件に一致した最初のボーンへ補正を適用します。
        /// </summary>
        private static bool TryApplyScaleToFirstMatch(
            IEnumerable<Transform> bones,
            Func<Transform, bool> predicate,
            Vector3 scaleModifier,
            List<Transform> removalTarget,
            List<string> logs,
            Transform costumeRoot,
            string modifierKey,
            string matchLabel,
            ref int appliedCount
        )
        {
            foreach (var bone in bones)
            {
                if (bone == null || !predicate(bone))
                {
                    continue;
                }

                return OCTCostumeScaleApplyUtility.TryApplyScaleToBone(
                    bone,
                    scaleModifier,
                    removalTarget,
                    logs,
                    costumeRoot,
                    modifierKey,
                    matchLabel,
                    ref appliedCount
                );
            }

            return false;
        }

    }
}
#endif
