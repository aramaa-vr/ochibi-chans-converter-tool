#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar 連携に関する「検出・バージョン警告」の安全ガードです。
// - 推奨バージョンとの差分を警告しつつ、変換自体は止めない方針を担います。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 推奨範囲は RecommendedVersionMin〜RecommendedVersionMax です。
// - バージョンが不明でも MA 型が見つかれば「検出あり」と判定します。
// - ここでの警告はユーザーへの注意喚起であり、処理停止条件ではありません。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MA バージョン運用方針を変更する場合はこのファイルを起点に統一する。
// - warning は呼び出し側（UI / 変換処理）で必要範囲に限定して出す。
// - Unity API 互換性を優先し、PackageManager 呼び出しは最小限に保つ。
// ============================================================================

using System;
using System.Collections.Generic;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA 連携の可用性判定と推奨バージョン警告を提供します。
    /// </summary>
    internal static class OCTModularAvatarIntegrationGuard
    {
        internal const string ModularAvatarPackageName = "nadena.dev.modular-avatar";
        internal const string RecommendedVersionMin = "1.16.2";
        internal const string RecommendedVersionMax = "1.16.3";
        internal const string RecommendedVersionRangeLabel = ">= " + RecommendedVersionMin + ", < " + RecommendedVersionMax;

        /// <summary>
        /// MA が検出されているかを返します（Package 情報 or 型検出）。
        /// </summary>
        internal static bool IsModularAvatarDetected()
        {
            if (TryGetInstalledModularAvatarVersion(out _))
            {
                return true;
            }

            return IsModularAvatarTypeDetected();
        }

        /// <summary>
        /// インストール済み MA バージョンを取得できた時のみ true。
        /// </summary>
        internal static bool TryGetInstalledModularAvatarVersion(out string version)
        {
            version = null;
            return TryFindInstalledVersion(out version);
        }

        /// <summary>
        /// 推奨バージョン不一致かを UI 表示用途で返します。
        /// </summary>
        internal static bool TryGetRecommendedVersionMismatch(out string installedVersion)
        {
            installedVersion = null;
            if (!TryGetInstalledModularAvatarVersion(out var installed))
            {
                return false;
            }

            installedVersion = installed;
            return !IsVersionInRecommendedRange(installed);
        }

        /// <summary>
        /// 不一致/不明のバージョン警告を logs に追加します。
        /// </summary>
        internal static void AppendVersionWarningIfNeeded(List<string> logs)
        {
            if (logs == null)
            {
                return;
            }

            if (!TryGetInstalledModularAvatarVersion(out var installed))
            {
                if (IsModularAvatarTypeDetected())
                {
                    logs.Add(OCTLocalization.Format("Log.ModularAvatarVersionUnknown", RecommendedVersionRangeLabel));
                }
                return;
            }

            if (!IsVersionInRecommendedRange(installed))
            {
                logs.Add(OCTLocalization.Format("Log.ModularAvatarVersionMismatch", installed, RecommendedVersionRangeLabel));
            }
        }


        /// <summary>
        /// MA 関連型が 1 つでも解決できるかを返します。
        /// </summary>
        private static bool IsModularAvatarTypeDetected()
        {
            return OCTModularAvatarReflection.TryGetBoneProxyType(out _)
                   || OCTModularAvatarReflection.TryGetMergeArmatureType(out _)
                   || OCTModularAvatarReflection.TryGetMeshSettingsType(out _);
        }

        /// <summary>
        /// 推奨範囲（>= RecommendedVersionMin かつ < RecommendedVersionMax）に収まっているかを判定します。
        /// 比較は .NET 標準の Version 比較を利用します。
        /// </summary>
        private static bool IsVersionInRecommendedRange(string installed)
        {
            if (!TryParseComparableVersion(installed, out var installedVersion))
            {
                return false;
            }

            if (!TryParseComparableVersion(RecommendedVersionMin, out var minVersion))
            {
                return false;
            }

            if (!TryParseComparableVersion(RecommendedVersionMax, out var maxVersion))
            {
                return false;
            }

            return installedVersion.CompareTo(minVersion) >= 0
                   && installedVersion.CompareTo(maxVersion) < 0;
        }

        /// <summary>
        /// 比較可能な Version を生成します（pre-release / metadata は無視）。
        /// </summary>
        private static bool TryParseComparableVersion(string version, out Version parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            // 例: 1.16.2-preview.1 / 1.16.2+meta -> 1.16.2
            var core = version.Split('-', '+')[0];
            return Version.TryParse(core, out parsed);
        }

        /// <summary>
        /// PackageManager から MA パッケージ情報を取得します。
        /// </summary>
        private static bool TryFindInstalledVersion(out string version)
        {
            version = null;

            try
            {
                var assetPath = $"Packages/{ModularAvatarPackageName}/package.json";
                var byAsset = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (byAsset != null && string.Equals(byAsset.name, ModularAvatarPackageName, StringComparison.Ordinal))
                {
                    version = byAsset.version;
                    return !string.IsNullOrEmpty(version);
                }

                var pkgs = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                if (pkgs == null)
                {
                    return false;
                }

                for (int i = 0; i < pkgs.Length; i++)
                {
                    var p = pkgs[i];
                    if (p == null) continue;

                    if (string.Equals(p.name, ModularAvatarPackageName, StringComparison.Ordinal))
                    {
                        version = p.version;
                        return !string.IsNullOrEmpty(version);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
