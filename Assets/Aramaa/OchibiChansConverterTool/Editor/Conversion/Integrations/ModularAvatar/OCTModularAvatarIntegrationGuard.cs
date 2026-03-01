#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal static class OCTModularAvatarIntegrationGuard
    {
        internal const string ModularAvatarPackageName = "nadena.dev.modular-avatar";
        internal const string RecommendedVersion = "1.16.2";

        private static bool _cached;
        private static bool _found;
        private static string _installedVersion;

        private static bool _warnedMismatch;
        private static bool _warnedUnknown;

        internal static bool IsModularAvatarDetected()
        {
            if (TryGetInstalledModularAvatarVersion(out _))
            {
                return true;
            }

            return OCTModularAvatarReflection.TryGetBoneProxyType(out _)
                   || OCTModularAvatarReflection.TryGetMergeArmatureType(out _)
                   || OCTModularAvatarReflection.TryGetMeshSettingsType(out _);
        }

        internal static bool TryGetInstalledModularAvatarVersion(out string version)
        {
            EnsureCachedPackageInfo();
            version = _installedVersion;
            return _found && !string.IsNullOrEmpty(version);
        }

        internal static void AppendVersionWarningIfNeeded(List<string> logs)
        {
            if (!TryGetInstalledModularAvatarVersion(out var installed))
            {
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
                var assetPath = $"Packages/{ModularAvatarPackageName}/package.json";
                var byAsset = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (byAsset != null && string.Equals(byAsset.name, ModularAvatarPackageName, StringComparison.Ordinal))
                {
                    _found = true;
                    _installedVersion = byAsset.version;
                    return;
                }

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
