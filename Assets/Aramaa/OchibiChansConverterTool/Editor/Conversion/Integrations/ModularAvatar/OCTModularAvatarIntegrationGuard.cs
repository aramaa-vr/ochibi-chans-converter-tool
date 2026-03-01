#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar 連携の安全ガード。
    ///
    /// 目的:
    /// - 推奨バージョンチェック（警告のみ。動作は継続）
    /// - 連携の強制無効化（Scripting Define / EditorPrefs）
    /// - Unity 2022.3.22f1 で存在する UnityEditor.PackageManager.PackageInfo API だけを使用する
    ///
    /// 注意:
    /// - PackageInfo.FindForPackageName は 2022.3.22f1 の PackageInfo に存在しないため使用しません
    /// - UnityEditor.PackageInfo と衝突しないよう、必ず完全修飾名で参照します
    /// </summary>
    internal static class OCTModularAvatarIntegrationGuard
    {
        internal const string ModularAvatarPackageName = "nadena.dev.modular-avatar";
        internal const string RecommendedVersion = "1.16.2";

        private const string DisableIntegrationEditorPrefsKey = "Aramaa.OchibiChansConverterTool.DisableModularAvatarIntegration";

        private static bool _cached;
        private static bool _found;
        private static string _installedVersion;

        private static bool _warnedMismatch;
        private static bool _warnedUnknown;

        internal static bool IsIntegrationDisabled
        {
            get
            {
#if OCT_DISABLE_MA_INTEGRATION
                return true;
#else
                return EditorPrefs.GetBool(DisableIntegrationEditorPrefsKey, false);
#endif
            }
        }

        internal static void SetIntegrationDisabled(bool disabled)
        {
            EditorPrefs.SetBool(DisableIntegrationEditorPrefsKey, disabled);
        }

        /// <summary>
        /// MA が検出できるか（パッケージ or 型のどちらかで判定）
        /// </summary>
        internal static bool IsModularAvatarDetected()
        {
            if (TryGetInstalledModularAvatarVersion(out _))
            {
                return true;
            }

            // 版ズレや特殊導入でパッケージ情報が取れない場合のフォールバック
            return OCTModularAvatarReflection.TryGetBoneProxyType(out _)
                   || OCTModularAvatarReflection.TryGetMergeArmatureType(out _)
                   || OCTModularAvatarReflection.TryGetMeshSettingsType(out _);
        }

        /// <summary>
        /// インストール済みの MA バージョンを取得します（取得できた場合のみ true）。
        /// </summary>
        internal static bool TryGetInstalledModularAvatarVersion(out string version)
        {
            EnsureCachedPackageInfo();
            version = _installedVersion;
            return _found && !string.IsNullOrEmpty(version);
        }

        /// <summary>
        /// 推奨バージョンとの差分があれば警告を追加します（警告のみ。動作は継続）。
        /// </summary>
        internal static void AppendVersionWarningIfNeeded(List<string> logs)
        {
            if (IsIntegrationDisabled)
            {
                logs?.Add(OCTLocalization.Get("Log.ModularAvatarDisabled"));
                return;
            }

            if (!TryGetInstalledModularAvatarVersion(out var installed))
            {
                // MA の型は存在するのに PackageInfo からバージョンが取れない場合は Unknown 扱い
                if (IsModularAvatarDetected())
                {
                    logs?.Add(OCTLocalization.Format("Log.ModularAvatarVersionUnknown", RecommendedVersion));
                    WarnUnknownOnce();
                }
                return;
            }

            if (!string.Equals(installed, RecommendedVersion, StringComparison.Ordinal))
            {
                logs?.Add(OCTLocalization.Format("Log.ModularAvatarVersionMismatch", installed, RecommendedVersion));
                WarnMismatchOnce(installed);
            }
        }

        /// <summary>
        /// 2022.3.22f1 の UnityEditor.PackageManager.PackageInfo を使って MA の version を取得しキャッシュします。
        /// </summary>
        private static void EnsureCachedPackageInfo()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            _found = false;
            _installedVersion = null;

            try
            {
                // 1) 最短: Packages/ 以下の assetPath から検索
                //    FindForAssetPath は 2022.3.22f1 の PackageInfo に存在する API です。
                //    見つからなければ null を返します。
                var assetPath = $"Packages/{ModularAvatarPackageName}/package.json";
                var byAsset = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (byAsset != null && string.Equals(byAsset.name, ModularAvatarPackageName, StringComparison.Ordinal))
                {
                    _found = true;
                    _installedVersion = byAsset.version;
                    return;
                }

                // 2) フォールバック: 登録済みパッケージ一覧を走査
                var pkgs = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                if (pkgs == null)
                {
                    return;
                }

                for (int i = 0; i < pkgs.Length; i++)
                {
                    var p = pkgs[i];
                    if (p == null) continue;

                    if (string.Equals(p.name, ModularAvatarPackageName, StringComparison.Ordinal))
                    {
                        _found = true;
                        _installedVersion = p.version;
                        return;
                    }
                }
            }
            catch
            {
                // 取得失敗は「不明」に倒す（ツールを落とさない）
                _found = false;
                _installedVersion = null;
            }
        }

        private static void WarnMismatchOnce(string installed)
        {
            if (_warnedMismatch) return;
            _warnedMismatch = true;

            Debug.LogWarning($"[OchibiChansConverterTool] Modular Avatar version mismatch. Installed: {installed}, recommended: {RecommendedVersion}. Integration will continue, but compatibility is not guaranteed.");
        }

        private static void WarnUnknownOnce()
        {
            if (_warnedUnknown) return;
            _warnedUnknown = true;

            Debug.LogWarning($"[OchibiChansConverterTool] Modular Avatar is present, but its version could not be determined. Recommended: {RecommendedVersion}.");
        }
    }
}
#endif
