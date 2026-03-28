#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 複製名の付与/正規化ロジックをまとめるユーティリティです
// - 変換パイプライン本体（OCTConversionPipeline）から命名責務を分離します
// ============================================================================

using System;

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
    }
}
#endif
