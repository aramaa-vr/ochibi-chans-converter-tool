#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    [InitializeOnLoad]
    internal static class OCTModularAvatarStartupValidator
    {
        static OCTModularAvatarStartupValidator()
        {
            EditorApplication.delayCall += SafeValidate;
        }

        private static void SafeValidate()
        {
            try
            {
                OCTModularAvatarIntegrationGuard.AppendVersionWarningIfNeeded(null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OchibiChansConverterTool] Modular Avatar startup validation failed (ignored): {e.Message}");
            }
        }
    }
}
#endif
