#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar 連携の「安全ガード」層。
    /// - 推奨バージョンチェック（警告のみ）
    /// - 連携の強制無効化（Scripting Define / EditorPrefs）
    /// - Unity 起動時に例外で落とさない
    /// </summary>
    internal static class OCTModularAvatarIntegrationGuard
    {
        internal const string ModularAvatarPackageName = "nadena.dev.modular-avatar";
        internal const string RecommendedVersion = "1.16.2";

        private const string DisableIntegrationEditorPrefsKey = "Aramaa.OchibiChansConverterTool.DisableModularAvatarIntegration";

        private static bool _warnedInConsole;

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

        internal static bool TryGetInstalledModularAvatarVersion(out string version)
        {
            version = null;
            try
            {
                var pkg = PackageInfo.FindForPackageName(ModularAvatarPackageName);
                if (pkg == null) return false;

                version = pkg.version;
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                // PackageManager が利用できない/例外の場合は「不明」として扱う
                return false;
            }
        }

        internal static bool IsModularAvatarDetected()
        {
            // まずは PackageManager で検出（VPM/UPM の正規導入）
            try
            {
                if (PackageInfo.FindForPackageName(ModularAvatarPackageName) != null)
                {
                    return true;
                }
            }
            catch
            {
                // 無視してリフレクション検出へ
            }

            // Assets 配下等で導入されている場合もあるため、型存在でも検出する
            return OCTModularAvatarReflection.TryGetBoneProxyType(out _)
                   || OCTModularAvatarReflection.TryGetMergeArmatureType(out _)
                   || OCTModularAvatarReflection.TryGetMeshSettingsType(out _);
        }

        internal static void AppendVersionWarningIfNeeded(System.Collections.Generic.List<string> logs)
        {
            if (IsIntegrationDisabled)
            {
                return;
            }

            // MA が無い環境では何もしない
            if (!IsModularAvatarDetected())
            {
                return;
            }

            if (TryGetInstalledModularAvatarVersion(out var installed))
            {
                if (!string.Equals(installed, RecommendedVersion, StringComparison.Ordinal))
                {
                    logs?.Add(OCTLocalization.Format("Log.ModularAvatarVersionMismatch", installed, RecommendedVersion));
                    WarnOnceInConsole($"[OchibiChansConverterTool] Modular Avatar version mismatch. Installed: {installed}, recommended: {RecommendedVersion}. Integration will try to run, but compatibility is not guaranteed.");
                }
            }
            else
            {
                logs?.Add(OCTLocalization.Format("Log.ModularAvatarVersionUnknown", RecommendedVersion));
                WarnOnceInConsole($"[OchibiChansConverterTool] Modular Avatar is detected but its version could not be determined. Recommended: {RecommendedVersion}.");
            }
        }

        private static void WarnOnceInConsole(string message)
        {
            if (_warnedInConsole) return;
            _warnedInConsole = true;
            Debug.LogWarning(message);
        }
    }
}
#endif
