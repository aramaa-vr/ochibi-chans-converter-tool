#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - MA 連携の入口クラスです。
// - 「MA が使えるか」の判定、スキップログ、各処理クラスへの委譲を担当します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 呼び出し側（Pipeline）はこのクラス経由で MA 機能を使ってください。
// - MA 未導入時は安全にスキップするため、null 参照や型未解決で落ちません。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MA 有効判定やスキップ文言の責務を他クラスへ散らさない。
// - 入口の API 形状を変える場合は Pipeline 側呼び出しも同時に更新する。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA 関連処理のエントリポイントです。
    /// </summary>
    internal static class OCTModularAvatarUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);

        /// <summary>
        /// MA が利用可能か（Package/型検出ベース）
        /// </summary>
        internal static bool IsModularAvatarAvailable
        {
            get { return OCTModularAvatarIntegrationGuard.IsModularAvatarDetected(); }
        }

        /// <summary>
        /// MABoneProxy 未実行時の標準スキップログを追加します。
        /// </summary>
        internal static void AppendBoneProxySkippedLog(List<string> logs)
        {
            if (logs == null) return;
            logs.Add(L("Log.MaboneProxySkipped"));
        }

        /// <summary>
        /// MA 全体スキップ時の標準ログを追加します。
        /// </summary>
        internal static void AppendModularAvatarSkippedLog(List<string> logs)
        {
            if (logs == null) return;
            logs.Add(L("Log.ModularAvatarMissing"));
        }

        /// <summary>
        /// BoneProxy 疑似処理を実行します。
        /// </summary>
        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            OCTModularAvatarBoneProxyUtility.ProcessBoneProxies(avatarRoot, logs);
        }

        /// <summary>
        /// MA Mesh Settings を起点に衣装補正（Scale -> BlendShape）を実行します。
        /// </summary>
        public static bool AdjustCostumeScalesForModularAvatarMeshSettings(
            GameObject dstRoot,
            GameObject basePrefabRoot,
            List<string> logs = null
        )
        {
            if (dstRoot == null || basePrefabRoot == null)
            {
                return false;
            }

            if (!IsModularAvatarAvailable)
            {
                AppendModularAvatarSkippedLog(logs);
                return true;
            }

            OCTModularAvatarIntegrationGuard.AppendVersionWarningIfNeeded(logs);

            var costumeRoots = OCTModularAvatarCostumeDetector.CollectCostumeRoots(dstRoot);
            OCTModularAvatarCostumeScaleAdjuster.AdjustByMergeArmatureMapping(dstRoot, logs);
            return OCTCostumeBlendShapeAdjuster.AdjustCostumeBlendShapes(
                basePrefabRoot,
                costumeRoots,
                logs
            );
        }
    }
}
#endif
