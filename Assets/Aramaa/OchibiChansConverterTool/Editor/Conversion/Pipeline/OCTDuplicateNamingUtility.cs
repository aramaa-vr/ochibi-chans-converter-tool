#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 複製名の付与/正規化ロジックをまとめるユーティリティです
// - 変換パイプライン本体（OCTConversionPipeline）から命名責務を分離します
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 複製時の命名ルールを提供します。
    /// </summary>
    internal static class OCTDuplicateNamingUtility
    {
        private const string DuplicatedNameTag = "(Ochibi-chans)";

        /// <summary>
        /// 重複したタグ付与を避けながら複製先オブジェクト名を決定します。
        /// </summary>
        public static string BuildDuplicateNameWithSuffix(string sourceName)
        {
            var duplicatedNameSuffixWithSpace = " " + DuplicatedNameTag;

            // 元名が空の場合はタグだけで安全な名前を作る。
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return DuplicatedNameTag;
            }

            var normalizedSourceName = sourceName.TrimEnd();

            // 既に同じ接尾辞で終わっている場合は、一度取り除いてから再付与する。
            // （末尾空白や揺れをならす）
            if (normalizedSourceName.EndsWith(duplicatedNameSuffixWithSpace, StringComparison.Ordinal))
            {
                normalizedSourceName = normalizedSourceName.Substring(0, normalizedSourceName.Length - duplicatedNameSuffixWithSpace.Length).TrimEnd();

                if (string.IsNullOrWhiteSpace(normalizedSourceName))
                {
                    return DuplicatedNameTag;
                }

                return normalizedSourceName + duplicatedNameSuffixWithSpace;
            }

            // 文字列内にタグが既に存在する場合は、追加せずそのまま採用する。
            if (normalizedSourceName.IndexOf(DuplicatedNameTag, StringComparison.Ordinal) >= 0)
            {
                return normalizedSourceName;
            }

            return normalizedSourceName + duplicatedNameSuffixWithSpace;
        }

        /// <summary>
        /// 複製直後オブジェクト自身を除外した兄弟集合に対して、重複しない名前を返します。
        /// </summary>
        public static string BuildUniqueDuplicateNameForSibling(GameObject duplicatedObject, string sourceName)
        {
            var desiredName = BuildDuplicateNameWithSuffix(sourceName);
            if (duplicatedObject == null)
            {
                return desiredName;
            }

            var parent = duplicatedObject.transform != null ? duplicatedObject.transform.parent : null;
            if (parent == null)
            {
                return desiredName;
            }

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling == null || sibling.gameObject == null || sibling.gameObject == duplicatedObject)
                {
                    continue;
                }

                usedNames.Add(sibling.gameObject.name);
            }

            if (!usedNames.Contains(desiredName))
            {
                return desiredName;
            }

            for (int suffixNumber = 1; ; suffixNumber++)
            {
                var candidate = $"{desiredName} ({suffixNumber})";
                if (!usedNames.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
#endif
