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
        internal static string FindPreferredPrefabPathUnder(string folder)
        {
            var candidates = CollectPrefabPaths(folder, sameDirectoryOnly: false);
            if (candidates.Count == 0) return null;

            var preferred = PickPrefabByFilenamePattern(candidates, "Kisekae Variant");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            preferred = PickPrefabByFilenamePattern(candidates, "Kaihen_Kisekae");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            preferred = PickPrefabByFilenamePattern(candidates, "Kisekae");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            return candidates[0];
        }

        internal static string FindPreferredKisekaeSiblingPrefabPath(
            string sourcePrefabPath,
            Func<string, bool> candidatePredicate)
        {
            if (string.IsNullOrEmpty(sourcePrefabPath)) return null;

            var directory = Path.GetDirectoryName(sourcePrefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(directory)) return null;

            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePrefabPath) ?? string.Empty;
            var candidates = CollectPrefabPaths(directory, sameDirectoryOnly: true)
                .Where(path =>
                    Path.GetFileNameWithoutExtension(path).IndexOf("kisekae", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(path => candidatePredicate == null || candidatePredicate(path))
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0) return null;

            var preferred = PickPrefabByFilenamePattern(candidates, sourceFileName);
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            return PickPrefabByFilenamePattern(candidates, "kisekae");
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
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { normalizedFolder });
            if (prefabGuids == null || prefabGuids.Length == 0) return new List<string>();

            var candidates = new List<string>();
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                if (sameDirectoryOnly)
                {
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
