#if UNITY_EDITOR
using System;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    // -----------------------------------------------------------------------------
    // 概要
    // - このファイルの責務:
    //   - バージョン文字列を解析し、現在バージョンと最新バージョンの関係を判定します。
    //   - 安定版判定に利用できるよう、安定版の Version へ正規化して返す機能を提供します。
    // - このファイルがしないこと:
    //   - ネットワーク通信、ログ出力、ローカライズ文言生成は行いません。
    // - 主要な入出力:
    //   - 入力: "1.2.3" / "1.2.3-beta.1" のような文字列。
    //   - 出力: OchibiChansConverterToolVersionStatus または Version。
    //   - 失敗時: 例外を投げず、Unknown または false を返します。
    // - 想定ユースケース:
    //   - latest.json から取得した最新バージョン文字列と、導入済みバージョン文字列を比較する用途。
    //
    // 重要メモ（初心者向け）
    // - この比較器は安定版/プレリリースを含めて順序比較を行います。
    // - 比較は文字列比較ではなく Version.CompareTo を利用します。
    //   そのため 1.10.0 と 1.2.0 の順序を正しく判定できます。
    // - Build metadata（+以降）はサポート対象外として解析失敗にします。
    // - 不正入力（null/空/形式不正）はフェイルセーフで Unknown/false を返します。
    //
    // チーム開発向けルール
    // - 比較規約（反射律・推移律）を崩さないため、コア比較は Version.CompareTo を維持してください。
    // - プレリリース判定は「ハイフン以降に識別子があるか」のみを使っています。
    //   この仕様を変える場合は、呼び出し側の更新通知ポリシーも同時に確認してください。
    // - 例外で制御せず、結果値で判定可能にする現在方針（TryParse 系）を維持してください。
    // -----------------------------------------------------------------------------

    /// <summary>
    /// バージョン文字列の解析と比較ロジックを担当します。
    /// 想定フォーマット: xx.yy.zz / xx.yy.zz-beta.n（n は数字）
    /// 旧形式 xx.yy.zz-beta は後方互換のためにも許可せず、形式不正として扱います。
    /// </summary>
    internal static class OchibiChansConverterToolVersionComparer
    {
        /// <summary>
        /// 現在バージョンと最新バージョンの関係を判定します。
        /// </summary>
        /// <param name="currentVersion">
        /// 現在使用中のバージョン文字列。
        /// null/空白のみは無効入力として扱います。
        /// </param>
        /// <param name="latestVersion">
        /// 配布元から取得した最新バージョン文字列。
        /// null/空白のみは無効入力として扱います。
        /// </param>
        /// <returns>
        /// 判定結果を表す <see cref="OchibiChansConverterToolVersionStatus"/>。
        /// 入力不正や解析不能時は <see cref="OchibiChansConverterToolVersionStatus.Unknown"/> を返します。
        /// </returns>
        /// <remarks>
        /// 例外は送出せず、結果値で失敗を通知します。
        /// プレリリース（beta.N）同士も含めて順序比較を行います。
        /// currentVersion が形式不正でも latestVersion を解析できる場合は、
        /// 安全側に倒して UpdateAvailable を返します。
        /// </remarks>
        public static OchibiChansConverterToolVersionStatus GetVersionStatus(string currentVersion, string latestVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(latestVersion))
            {
                return OchibiChansConverterToolVersionStatus.Unknown;
            }

            if (!TryParseVersion(latestVersion, out var latest))
            {
                return OchibiChansConverterToolVersionStatus.Unknown;
            }

            if (!TryParseVersion(currentVersion, out var current))
            {
                return OchibiChansConverterToolVersionStatus.UpdateAvailable;
            }

            return CompareVersions(current, latest);
        }

        /// <summary>
        /// バージョン文字列を安定版（プレリリースではない）として解析します。
        /// </summary>
        /// <param name="versionText">
        /// 解析対象のバージョン文字列。
        /// null/空白、形式不正、プレリリース表記（-を含む）は失敗します。
        /// </param>
        /// <param name="stableVersion">
        /// 解析成功時に正規化済み <see cref="Version"/> を返します。
        /// 失敗時は null です。
        /// </param>
        /// <returns>
        /// 安定版として解析できた場合は true、それ以外は false。
        /// </returns>
        /// <remarks>
        /// Build metadata（+以降）はサポート対象外です。
        /// </remarks>
        public static bool TryParseStableVersion(string versionText, out Version stableVersion)
        {
            stableVersion = null;
            if (!TryParseVersion(versionText, out var parts))
            {
                return false;
            }

            if (parts.IsPreRelease)
            {
                return false;
            }

            stableVersion = parts.CoreVersion;
            return true;
        }

        private static bool TryParseVersion(string version, out VersionParts parsed)
        {
            parsed = default;

            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var trimmed = version.Trim();

            var plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
            {
                return false;
            }

            var withoutBuild = trimmed;

            string corePart;
            string preReleasePart = null;
            var hyphenIndex = withoutBuild.IndexOf('-');
            if (hyphenIndex >= 0)
            {
                corePart = withoutBuild.Substring(0, hyphenIndex);
                if (hyphenIndex + 1 >= withoutBuild.Length)
                {
                    return false;
                }

                preReleasePart = withoutBuild.Substring(hyphenIndex + 1);
            }
            else
            {
                corePart = withoutBuild;
            }

            if (!TryParseCoreVersion(corePart, out var coreVersion))
            {
                return false;
            }

            if (!TryParseIdentifiers(preReleasePart, out var isPreRelease, out var preReleaseNumber))
            {
                return false;
            }

            parsed = new VersionParts(coreVersion, isPreRelease, preReleaseNumber);
            return true;
        }

        private static bool TryParseCoreVersion(string corePart, out Version normalizedVersion)
        {
            normalizedVersion = null;
            if (string.IsNullOrWhiteSpace(corePart))
            {
                return false;
            }

            // このツールは major.minor.patch の 3 セグメントのみを受け付ける。
            var segments = corePart.Split('.');
            if (segments.Length != 3)
            {
                return false;
            }

            if (!Version.TryParse(corePart, out var parsedCore))
            {
                return false;
            }

            var build = parsedCore.Build >= 0 ? parsedCore.Build : 0;
            var revision = parsedCore.Revision >= 0 ? parsedCore.Revision : 0;
            normalizedVersion = new Version(parsedCore.Major, parsedCore.Minor, build, revision);
            return true;
        }

        private static bool TryParseIdentifiers(string identifiers, out bool hasIdentifiers, out string preReleaseNumber)
        {
            hasIdentifiers = false;
            preReleaseNumber = null;
            if (string.IsNullOrEmpty(identifiers))
            {
                return true;
            }

            var raw = identifiers.Split('.');

            // 旧形式の "-beta"（beta のみ、数値サフィックスなし）は明示的に非対応。
            // 許可するプレリリースは "beta.<number>" のみ。
            if (raw.Length != 2 || raw[0] != "beta")
            {
                return false;
            }

            if (!IsValidNumericIdentifier(raw[1]))
            {
                return false;
            }

            hasIdentifiers = true;
            preReleaseNumber = raw[1];

            return true;
        }

        private static OchibiChansConverterToolVersionStatus CompareVersions(VersionParts current, VersionParts latest)
        {
            var coreComparison = current.CoreVersion.CompareTo(latest.CoreVersion);
            if (coreComparison != 0)
            {
                return coreComparison < 0 ? OchibiChansConverterToolVersionStatus.UpdateAvailable : OchibiChansConverterToolVersionStatus.Ahead;
            }

            if (current.IsPreRelease != latest.IsPreRelease)
            {
                return current.IsPreRelease ? OchibiChansConverterToolVersionStatus.UpdateAvailable : OchibiChansConverterToolVersionStatus.Ahead;
            }

            if (!current.IsPreRelease)
            {
                return OchibiChansConverterToolVersionStatus.UpToDate;
            }

            var preReleaseComparison = CompareNumericIdentifiers(current.PreReleaseNumber, latest.PreReleaseNumber);
            if (preReleaseComparison == 0)
            {
                return OchibiChansConverterToolVersionStatus.UpToDate;
            }

            return preReleaseComparison < 0
                ? OchibiChansConverterToolVersionStatus.UpdateAvailable
                : OchibiChansConverterToolVersionStatus.Ahead;
        }

        private static int CompareNumericIdentifiers(string left, string right)
        {
            // 防御的実装: 想定外の入力でも例外を投げず、決定的な結果を返す。
            if (!IsValidNumericIdentifier(left) || !IsValidNumericIdentifier(right))
            {
                return string.CompareOrdinal(left ?? string.Empty, right ?? string.Empty);
            }

            if (left.Length != right.Length)
            {
                return left.Length.CompareTo(right.Length);
            }

            return string.CompareOrdinal(left, right);
        }

        private static bool IsValidNumericIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    return false;
                }
            }

            // SemVer numeric identifier と同様に、先頭ゼロ（"0" 以外）は不許可。
            return value.Length == 1 || value[0] != '0';
        }

        private readonly struct VersionParts
        {
            public VersionParts(Version coreVersion, bool isPreRelease, string preReleaseNumber)
            {
                CoreVersion = coreVersion;
                IsPreRelease = isPreRelease;
                PreReleaseNumber = preReleaseNumber;
            }

            public Version CoreVersion { get; }
            public bool IsPreRelease { get; }
            public string PreReleaseNumber { get; }
        }
    }
}
#endif
