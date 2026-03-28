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
        private const string PatternKisekaeVariant = "Kisekae Variant";
        private const string PatternKisekae = "Kisekae";
        private const string PatternKisekaeLower = "kisekae";

        internal static string FindPreferredPrefabPathUnder(string folder)
        {
            // ドロップダウン候補の探索では「指定フォルダ配下すべて（子フォルダ含む）」を対象にする。
            var candidates = CollectPrefabPaths(folder, sameDirectoryOnly: false);
            if (candidates.Count == 0) return null;

            // 既存仕様の優先順を維持:
            // 1) "Kisekae Variant"  2) "Kisekae"  3) 先頭候補
            var preferred = PickPrefabByFilenamePattern(candidates, PatternKisekaeVariant);
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            preferred = PickPrefabByFilenamePattern(candidates, PatternKisekae);
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            return candidates[0];
        }

        internal static string FindPreferredKisekaeSiblingPrefabPath(
            string sourcePrefabPath,
            Func<string, bool> candidatePredicate)
        {
            if (string.IsNullOrEmpty(sourcePrefabPath)) return null;

            // OriginalAvatar 解決では「同一ディレクトリ内の兄弟 prefab」のみを見る。
            var directory = Path.GetDirectoryName(sourcePrefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(directory)) return null;

            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePrefabPath) ?? string.Empty;
            var candidates = CollectPrefabPaths(directory, sameDirectoryOnly: true)
                // kisekae を含む名前だけを候補化
                .Where(path =>
                    Path.GetFileNameWithoutExtension(path).IndexOf(PatternKisekaeLower, StringComparison.OrdinalIgnoreCase) >= 0)
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
            return PickPrefabByFilenamePattern(candidates, PatternKisekaeLower);
        }

        internal static string PickPrefabByFilenamePattern(IEnumerable<string> paths, string pattern)
        {
            if (paths == null) return null;
            if (string.IsNullOrEmpty(pattern)) return null;

            return paths.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path)
                    .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<string> CollectPrefabPaths(string folder, bool sameDirectoryOnly)
        {
            if (string.IsNullOrEmpty(folder)) return new List<string>();

            var normalizedFolder = folder.Replace("\\", "/");
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
                    var pathDirectory = Path.GetDirectoryName(path)?.Replace("\\", "/");
                    if (!string.Equals(pathDirectory, normalizedFolder, StringComparison.OrdinalIgnoreCase)) continue;
                }

                candidates.Add(path);
            }

            return candidates;
        }
    }
}
#endif
