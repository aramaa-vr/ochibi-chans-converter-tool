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
using UnityEngine.SceneManagement;

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

            // 3) 比較対象となる「同じ階層の名前」を集める。
            //    - 親がある場合: 同じ親の child（=兄弟）
            //    - 親がない場合: 同じ Scene の root GameObject 群
            //      （root 同士の重複もここで解決する）
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var parent = duplicatedObject.transform != null ? duplicatedObject.transform.parent : null;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    if (sibling == null || sibling.gameObject == null || sibling.gameObject == duplicatedObject)
                    {
                        continue;
                    }

                    usedNames.Add(sibling.gameObject.name);
                }
            }
            else
            {
                Scene scene = duplicatedObject.scene;
                if (scene.IsValid())
                {
                    var roots = scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        var root = roots[i];
                        if (root == null || root == duplicatedObject)
                        {
                            continue;
                        }

                        usedNames.Add(root.name);
                    }
                }
            }

            // 4) 同名が無ければそのまま採用。
            if (!usedNames.Contains(desiredName))
            {
                return desiredName;
            }

            // 5) 既に使われている場合は "(1)", "(2)"... の最小空き番号を採用。
            //    これにより連番飛びを最小化し、見通しの良い命名にする。
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
