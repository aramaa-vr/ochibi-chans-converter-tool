#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - Unity 起動後に MA 連携状態（主に推奨バージョン）を安全確認する初期化クラスです。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 起動時のチェックは「警告のみ」で、処理を止めません。
// - 例外が起きても editor 起動を妨げないよう catch して無視します。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 起動時処理は軽量に保つ（重い処理を足さない）。
// - 失敗時は throw せず warning のみ。
// ============================================================================

using System;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Domain Reload 後の遅延タイミングで MA バージョン警告をチェックします。
    /// </summary>
    [InitializeOnLoad]
    internal static class OCTModularAvatarStartupValidator
    {
        static OCTModularAvatarStartupValidator()
        {
            EditorApplication.delayCall += SafeValidate;
        }

        /// <summary>
        /// 例外を無視して安全に検証します。
        /// </summary>
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
