#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    // latest.json のみを対象にした最新版パーサーです。
    // JsonUtility で読みやすく保守しやすい固定スキーマを採用し、失敗時は null を返します。
    internal static class OCTLatestJsonParser
    {
        /// <summary>
        /// latest.json から対象パッケージの最新バージョンを抽出します。
        /// 安定版が存在する場合は安定版を優先し、安定版が存在しない場合のみ
        /// サポート対象のプレリリース（-beta.&lt;number&gt;）を返します。
        /// 想定フォーマット（JsonUtility で直接パース可能）:
        /// {
        ///   "schemaVersion": 1,
        ///   "packages": [
        ///     { "id": "jp.aramaa.ochibi-chans-converter-tool", "version": "0.5.3" }
        ///   ]
        /// }
        /// </summary>
        /// <remarks>
        /// 取得失敗・JSON 破損・対象 ID なし・不正バージョン時は null を返します。
        /// </remarks>
        public static string ExtractLatestVersion(string latestJsonText)
        {
            if (string.IsNullOrWhiteSpace(latestJsonText))
            {
                return null;
            }

            LatestJsonRoot root;
            try
            {
                root = JsonUtility.FromJson<LatestJsonRoot>(latestJsonText);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (root == null || root.schemaVersion != OCTEditorConstants.LatestJsonSchemaVersion
                || root.packages == null || root.packages.Length == 0)
            {
                return null;
            }

            Version latestStable = null;
            string latestStableText = null;
            Version latestPrereleaseCore = null;
            int latestPrereleaseNumber = -1;
            string latestPrereleaseText = null;

            for (var i = 0; i < root.packages.Length; i++)
            {
                var entry = root.packages[i];
                if (entry == null || !string.Equals(entry.id, OCTEditorConstants.TargetPackageId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!OCTVersionComparer.TryParseStableVersion(entry.version, out var candidate))
                {
                    if (TryParseSupportedBetaVersion(entry.version, out var prereleaseCore, out var prereleaseNumber))
                    {
                        var isNewerPrerelease = false;
                        if (latestPrereleaseCore == null)
                        {
                            isNewerPrerelease = true;
                        }
                        else
                        {
                            var coreComparison = prereleaseCore.CompareTo(latestPrereleaseCore);
                            isNewerPrerelease = coreComparison > 0
                                || (coreComparison == 0 && prereleaseNumber > latestPrereleaseNumber);
                        }

                        if (isNewerPrerelease)
                        {
                            latestPrereleaseCore = prereleaseCore;
                            latestPrereleaseNumber = prereleaseNumber;
                            latestPrereleaseText = entry.version;
                        }
                    }

                    continue;
                }

                if (latestStable == null || candidate.CompareTo(latestStable) > 0)
                {
                    latestStable = candidate;
                    latestStableText = entry.version;
                }
            }

            return latestStableText ?? latestPrereleaseText;
        }

        private static bool TryParseSupportedBetaVersion(string versionText, out Version coreVersion, out int betaNumber)
        {
            coreVersion = null;
            betaNumber = -1;

            if (string.IsNullOrWhiteSpace(versionText))
            {
                return false;
            }

            var separator = "-beta.";
            var separatorIndex = versionText.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                return false;
            }

            var coreText = versionText.Substring(0, separatorIndex);
            var suffix = versionText.Substring(separatorIndex + separator.Length);
            if (!OCTVersionComparer.TryParseStableVersion(coreText, out coreVersion))
            {
                return false;
            }

            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out betaNumber) || betaNumber < 0)
            {
                return false;
            }

            return true;
        }

        [Serializable]
        private sealed class LatestJsonRoot
        {
            public int schemaVersion;
            public LatestPackageEntry[] packages;
        }

        [Serializable]
        private sealed class LatestPackageEntry
        {
            public string id;
            public string version;
        }
    }
}
#endif
