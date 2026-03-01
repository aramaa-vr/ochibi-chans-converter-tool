#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Unity 起動直後（ドメインリロード直後）に、例外を出さずに MA 連携状態だけを確認します。
    /// 
    /// 目的：
    /// - MA 更新直後でも Editor 起動を壊さない
    /// - 推奨バージョン（1.16.2）との差分があれば、Console に警告を 1 回だけ出す
    /// </summary>
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
#if OCT_DISABLE_MA_INTEGRATION
                return;
#endif
                // logs は null（Console のみ）
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
