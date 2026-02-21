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
        private const float ScaleEpsilon = 0.0001f;
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
            new OCTConversionLogger(logs).Add("Log.CostumeScaleCriteria");

            var avatarBoneScaleModifiers = BuildAvatarBoneScaleModifiers(dstArmature, baseArmaturePaths, logs);
            if (avatarBoneScaleModifiers.Count == 0)
            {
                return true;
            }

            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            logs?.Add(L("Log.CostumeScaleHeader"));
            logs?.Add(F("Log.CostumeCount", costumeRoots.Count));

            foreach (var costumeRoot in costumeRoots)
            {
                AdjustOneCostume(costumeRoot, avatarBoneScaleModifiers, logs);
            }

            return true;
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

                paths.Add(GetStableTransformPathWithSiblingIndex(t, baseArmature));
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
                if (b == null || IsNearlyOne(b.localScale))
                {
                    continue;
                }

                // Armature 名は FBX 小物などで入れ子になりやすく、
                // ここに混ざると衣装側の Armature.1 へ誤適用されるリスクが高いです。
                // 「メイン Armature 自体」以外の同名 Transform は除外します。
                if (!ReferenceEquals(b, avatarArmature) && string.Equals(b.name, avatarArmature.name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (allowedArmaturePaths != null)
                {
                    var path = GetStableTransformPathWithSiblingIndex(b, avatarArmature);
                    var readablePath = GetTransformPath(b, avatarArmature);
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
            if (costumeRoot == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));
            if (avatarBoneScaleModifiers == null || avatarBoneScaleModifiers.Count == 0)
            {
                return;
            }

            int appliedCount = 0;
            var costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();
            var costumeArmature = OCTEditorUtility.FindAvatarMainArmature(costumeRoot);

            ApplyScaleModifiers(
                avatarBoneScaleModifiers,
                costumeBones,
                costumeArmature,
                logs,
                costumeRoot,
                ref appliedCount
            );

            logs?.Add(F("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }

        private static bool IsNearlyOne(Vector3 s)
        {
            return Mathf.Abs(s.x - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.y - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.z - 1f) < ScaleEpsilon;
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
                var modifierKeyForLog = BuildModifierKeyForLog(modifier);

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

                    if (TryApplyScaleToBone(
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
                // RelativePath が空のキーは「Armature ルート自身」を意味するため、
                // Armature-path 一致でのみ適用し、Contains フォールバックは行いません。
                if (!string.IsNullOrEmpty(modifier.RelativePath) && (costumeArmature == null || !string.Equals(modifier.Name, costumeArmature.name, StringComparison.Ordinal)))
                {
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
        }

        private static string BuildModifierKeyForLog(AvatarBoneScaleModifier modifier)
        {
            if (modifier == null)
            {
                return L("Log.NullValue");
            }

            if (string.IsNullOrEmpty(modifier.RelativePath))
            {
                return modifier.Name;
            }

            return $"{modifier.Name} ({modifier.RelativePath})";
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

                return TryApplyScaleToBone(
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

        /// <summary>
        /// 実際に1ボーンへスケール補正を反映し、ログ・dirty化を行います。
        /// </summary>
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

        private static string GetStableTransformPathWithSiblingIndex(Transform target, Transform root)
        {
            if (target == null)
            {
                return L("Log.NullValue");
            }

            if (root == null)
            {
                return target.name;
            }

            var segments = new List<string>();
            var current = target;

            while (current != null)
            {
                if (current == root)
                {
                    segments.Add(root.name);
                    break;
                }

                var parent = current.parent;
                if (parent == null)
                {
                    segments.Add(current.name);
                    break;
                }

                int ordinal = 0;
                int sameNameCount = 0;

                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == null || !string.Equals(child.name, current.name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (child == current)
                    {
                        ordinal = sameNameCount;
                    }

                    sameNameCount++;
                }

                segments.Add(sameNameCount > 1 ? $"{current.name}#{ordinal}" : current.name);
                current = parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
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
