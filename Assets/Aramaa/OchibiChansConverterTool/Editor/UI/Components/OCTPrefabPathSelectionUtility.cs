#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Prefab パス候補の収集/優先選択を共通化するユーティリティです。
    /// </summary>
    internal static class OCTPrefabPathSelectionUtility
    {
        private const string PrefabSearchFilter = "t:Prefab";
        private const string PatternKisekae = "Kisekae";

        /// <summary>
        /// 指定フォルダ配下（子フォルダ含む）から Prefab を収集し、既定の優先順位で1件選びます。
        /// </summary>
        internal static string FindPreferredPrefabPathUnder(string folder)
        {
            // ドロップダウン候補の探索では「指定フォルダ直下のみ」を対象にする。
            var candidates = CollectPrefabPaths(folder, sameDirectoryOnly: true);
            if (candidates.Count == 0) return null;

            // "Kisekae" を含む候補を優先し、無ければ先頭候補を返す。
            var preferred = PickPrefabByFilenamePattern(candidates, PatternKisekae);
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            return candidates[0];
        }

        /// <summary>
        /// 指定 Prefab と同一ディレクトリ内の kisekae 候補を収集し、優先順位に従って1件返します。
        /// </summary>
        /// <param name="sourcePrefabPath">基準となる Prefab パス。</param>
        /// <param name="candidatePredicate">候補に追加適用する条件（null なら条件なし）。</param>
        internal static string FindPreferredKisekaeSiblingPrefabPath(
            string sourcePrefabPath,
            Func<string, bool> candidatePredicate)
        {
            if (string.IsNullOrEmpty(sourcePrefabPath)) return null;

            // OriginalAvatar 解決では「同一ディレクトリ内の兄弟 prefab」のみを見る。
            var directory = NormalizeAssetPath(Path.GetDirectoryName(sourcePrefabPath));
            if (string.IsNullOrEmpty(directory)) return null;

            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePrefabPath) ?? string.Empty;
            var candidates = CollectPrefabPaths(directory, sameDirectoryOnly: true)
                // kisekae を含む名前だけを候補化
                .Where(path =>
                    Path.GetFileNameWithoutExtension(path).IndexOf(PatternKisekae, StringComparison.OrdinalIgnoreCase) >= 0)
                // 呼び出し側の追加条件（例: Descriptor必須）を適用
                .Where(path => candidatePredicate == null || candidatePredicate(path))
                // 同率時の結果が毎回ぶれないよう、先に安定ソートしておく
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0) return null;

            // まず「元 prefab 名を含むもの」を優先（例: Chiffon -> Chiffon_kisekae）。
            var preferred = PickPrefabByFilenamePattern(candidates, sourceFileName);
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            // それが無ければ kisekae 名称一致の先頭を採用。
            return PickPrefabByFilenamePattern(candidates, PatternKisekae);
        }

        /// <summary>
        /// ファイル名（拡張子除く）に指定パターンを含む最初の Prefab パスを返します。
        /// </summary>
        /// <remarks>
        /// 呼び出し側で候補順を整列しておくことで、返却結果を安定化できます。
        /// </remarks>
        internal static string PickPrefabByFilenamePattern(IEnumerable<string> paths, string pattern)
        {
            if (paths == null) return null;
            if (string.IsNullOrEmpty(pattern)) return null;

            // 部分一致で最初に見つかった候補を採用（大文字小文字は区別しない）。
            return paths.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path)
                    .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 指定フォルダから Prefab パス一覧を収集します。
        /// </summary>
        /// <param name="folder">探索対象フォルダ。</param>
        /// <param name="sameDirectoryOnly">
        /// true の場合は指定フォルダ直下のみ、false の場合は子フォルダも含めて収集します。
        /// </param>
        private static List<string> CollectPrefabPaths(string folder, bool sameDirectoryOnly)
        {
            if (string.IsNullOrEmpty(folder)) return new List<string>();

            var normalizedFolder = NormalizeAssetPath(folder);
            // AssetDatabase.FindAssets は sameDirectoryOnly=false なら子フォルダも探索する。
            var prefabGuids = AssetDatabase.FindAssets(PrefabSearchFilter, new[] { normalizedFolder });
            if (prefabGuids == null || prefabGuids.Length == 0) return new List<string>();

            var candidates = new List<string>();
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                if (sameDirectoryOnly)
                {
                    // sameDirectoryOnly=true の場合は直下のみ許可（子フォルダは除外）。
                    var pathDirectory = NormalizeAssetPath(Path.GetDirectoryName(path));
                    if (!string.Equals(pathDirectory, normalizedFolder, StringComparison.OrdinalIgnoreCase)) continue;
                }

                candidates.Add(path);
            }

            return candidates;
        }

        /// <summary>
        /// AssetDatabase 向けパス区切り（/）へ正規化します。
        /// </summary>
        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace("\\", "/");
        }
    }
}
#endif
