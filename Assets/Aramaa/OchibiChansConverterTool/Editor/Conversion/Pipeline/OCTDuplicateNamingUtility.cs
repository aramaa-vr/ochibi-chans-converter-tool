#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 複製名の付与/正規化ロジックをまとめるユーティリティです
// - 変換パイプライン本体（OCTConversionPipeline）から命名責務を分離します
// ============================================================================

using System;
using UnityEditor;
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
            // 1) まず「理想のベース名」を作る（例: Avatar (Ochibi-chans)）
            var desiredName = BuildDuplicateNameWithSuffix(sourceName);

            // 2) 呼び出し側の都合で null が渡ってきても落とさず、
            //    まずはベース名だけ返して処理継続できるようにする。
            if (duplicatedObject == null)
            {
                return desiredName;
            }

            // 3) Unity 標準の GetUniqueNameForSibling で一意名を取得する。
            //    ただしこの API は「自分自身」も衝突候補に入るため、
            //    先に一時名へ退避してから問い合わせることで、連番飛びを抑える。
            var parent = duplicatedObject.transform != null ? duplicatedObject.transform.parent : null;
            var originalName = duplicatedObject.name;
            var temporaryName = $"__oct_tmp__{Guid.NewGuid():N}";

            try
            {
                duplicatedObject.name = temporaryName;
                return GameObjectUtility.GetUniqueNameForSibling(parent, desiredName);
            }
            finally
            {
                // renameRule の呼び出し側で最終名を代入するまでの安全策として元名へ戻す。
                duplicatedObject.name = originalName;
            }
        }
    }
}
#endif
