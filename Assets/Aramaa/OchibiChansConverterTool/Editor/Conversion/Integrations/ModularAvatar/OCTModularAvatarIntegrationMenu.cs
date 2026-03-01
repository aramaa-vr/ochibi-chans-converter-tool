#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal static class OCTModularAvatarIntegrationMenu
    {
        // 既存の ToolsMenuPath は「ウィンドウを開く」メニューとして使っているため、サブメニュー化して壊さない。
        private const string MenuRoot = "Tools/Aramaa/Ochibi-chans Converter Tool/Modular Avatar";

        private const string DisableMenuPath = MenuRoot + "/Disable Integration";
        private const string ValidateMenuPath = MenuRoot + "/Validate Integration";

        [MenuItem(DisableMenuPath, priority = 10)]
        private static void ToggleDisable()
        {
            var next = !OCTModularAvatarIntegrationGuard.IsIntegrationDisabled;
            OCTModularAvatarIntegrationGuard.SetIntegrationDisabled(next);
        }

        [MenuItem(DisableMenuPath, validate = true)]
        private static bool ValidateToggleDisable()
        {
            Menu.SetChecked(DisableMenuPath, OCTModularAvatarIntegrationGuard.IsIntegrationDisabled);
            return true;
        }

        [MenuItem(ValidateMenuPath, priority = 20)]
        private static void ValidateIntegration()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Modular Avatar Integration Check ===");
            sb.AppendLine($"Integration disabled: {OCTModularAvatarIntegrationGuard.IsIntegrationDisabled}");
            sb.AppendLine($"Modular Avatar detected: {OCTModularAvatarIntegrationGuard.IsModularAvatarDetected()}");

            if (OCTModularAvatarIntegrationGuard.TryGetInstalledModularAvatarVersion(out var v))
            {
                sb.AppendLine($"Installed version: {v}");
            }
            else
            {
                sb.AppendLine("Installed version: (unknown)");
            }

            sb.AppendLine($"Recommended version: {OCTModularAvatarIntegrationGuard.RecommendedVersion}");

            sb.AppendLine();
            sb.AppendLine("Type probes:");
            sb.AppendLine($" - BoneProxy: {OCTModularAvatarReflection.TryGetBoneProxyType(out _)}");
            sb.AppendLine($" - MergeArmature: {OCTModularAvatarReflection.TryGetMergeArmatureType(out _)}");
            sb.AppendLine($" - MeshSettings: {OCTModularAvatarReflection.TryGetMeshSettingsType(out _)}");

            EditorUtility.DisplayDialog(
                "Ochibi-chans Converter Tool",
                sb.ToString(),
                "OK"
            );
        }

        [MenuItem(ValidateMenuPath, validate = true)]
        private static bool ValidateValidateIntegration()
        {
            return true;
        }
    }
}
#endif
