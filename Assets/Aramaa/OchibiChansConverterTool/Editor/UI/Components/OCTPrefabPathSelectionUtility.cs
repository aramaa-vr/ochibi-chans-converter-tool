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
        /// <summary>
        /// 着せ替え用 Prefab 名を推定するための暫定キーワードです。
        /// 本来はアバター製作者の命名言語に依存しない判定が望ましいですが、
        /// 既存資産との互換性を優先し、現状は "Kisekae" を大文字小文字無視で使用します。
        /// </summary>
        private const string PatternKisekae = "Kisekae";

        /// <summary>
        /// 指定フォルダ直下（子フォルダは含まない）から Prefab を収集し、既定の優先順位で1件選びます。
        /// </summary>
        internal static string FindPreferredPrefabPathUnder(string folder)
        {
            // ドロップダウン候補の探索では「指定フォルダ直下のみ」を対象にする。
            var candidates = CollectPrefabPaths(folder);
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
            var candidates = CollectPrefabPaths(directory)
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

            // それが無ければ kisekae 名称一致の中から優先規則（短い名前優先）で採用。
            return PickPrefabByFilenamePattern(candidates, PatternKisekae);
        }

        /// <summary>
        /// ファイル名（拡張子除く）に指定パターンを含む Prefab パスを優先規則に従って返します。
        /// </summary>
        /// <remarks>
        /// 一致候補が複数ある場合は、ファイル名が短いものを優先し、同率時はファイル名順で決定します。
        /// </remarks>
        internal static string PickPrefabByFilenamePattern(IEnumerable<string> paths, string pattern)
        {
            if (paths == null) return null;
            if (string.IsNullOrEmpty(pattern)) return null;

            // 条件一致が複数ある場合は、ファイル名（拡張子除く）の文字数が短いものを優先。
            // 同率時はファイル名順で安定化する。
            return paths
                .Where(path =>
                    Path.GetFileNameWithoutExtension(path)
                        .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(path => (Path.GetFileNameWithoutExtension(path) ?? string.Empty).Length)
                .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        /// <summary>
        /// 指定フォルダ直下（子フォルダは除外）の Prefab パス一覧を収集します。
        /// </summary>
        /// <param name="folder">探索対象フォルダ。</param>
        private static List<string> CollectPrefabPaths(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return new List<string>();

            var normalizedFolder = NormalizeAssetPath(folder);
            // FindAssets は子フォルダも返すため、後段で「直下のみ」に絞り込む。
            var prefabGuids = AssetDatabase.FindAssets(PrefabSearchFilter, new[] { normalizedFolder });
            if (prefabGuids == null || prefabGuids.Length == 0) return new List<string>();

            var candidates = new List<string>();
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                var pathDirectory = NormalizeAssetPath(Path.GetDirectoryName(path));
                if (!string.Equals(pathDirectory, normalizedFolder, StringComparison.OrdinalIgnoreCase)) continue;

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
