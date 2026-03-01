#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar 依存処理の「入口」を1箇所に集約するユーティリティです。
// - MA の存在判定（PackageManager/リフレクション）と、各専用クラスへの委譲を担当します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - このクラスは「依存有無の判断」と「処理の振り分け」だけを担当します。
// - 実処理は以下へ分離されています。
//   - MA連携: OCTModularAvatarBoneProxyUtility / OCTModularAvatarCostumeDetector
//   - MA非依存: OCTCostumeScaleAdjuster / OCTCostumeBlendShapeAdjuster
// - MA未導入時に落とさない（安全スキップ）ことを最優先にしています。
// - 処理順は「衣装スケール補正 -> BlendShape 同期」の順で固定です。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MA 連携の有効/無効判定は可能な限りこのクラスへ集約する。
// - 呼び出し元（Pipeline）には、業務フローのみを残し、実装詳細を漏らさない。
// - MA未導入時のログ文言（スキップ/不足）は既存キーを使って互換維持する。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar 関連処理のエントリポイント。
    /// MA の存在/無効化判定をここに集約する。
    /// </summary>
    internal static class OCTModularAvatarUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);

        internal static bool IsModularAvatarAvailable
        {
            get
            {
                if (OCTModularAvatarIntegrationGuard.IsIntegrationDisabled)
                {
                    return false;
                }

                return OCTModularAvatarIntegrationGuard.IsModularAvatarDetected();
            }
        }

        internal static void AppendBoneProxySkippedLog(List<string> logs)
        {
            if (logs == null) return;

            logs.Add(
                OCTModularAvatarIntegrationGuard.IsIntegrationDisabled
                    ? L("Log.MaboneProxySkippedDisabled")
                    : L("Log.MaboneProxySkipped")
            );
        }

        internal static void AppendModularAvatarSkippedLog(List<string> logs)
        {
            if (logs == null) return;

            logs.Add(
                OCTModularAvatarIntegrationGuard.IsIntegrationDisabled
                    ? L("Log.ModularAvatarDisabled")
                    : L("Log.ModularAvatarMissing")
            );
        }

        /// <summary>
        /// MABoneProxy を処理します。
        /// MAの有無チェックと「スキップログ」は呼び出し側でまとめて扱う想定です。
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
        /// MA Mesh Settings が付与された衣装ルートを検出し、衣装スケール調整と BlendShape 同期を行います。
        /// 処理順序は互換性のため固定です（Scale -> BlendShape）。
        /// Modular Avatar 未導入時は安全にスキップします。
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

            // バージョン不一致は警告のみ（処理は試みる）
            OCTModularAvatarIntegrationGuard.AppendVersionWarningIfNeeded(logs);

            var costumeRoots = OCTModularAvatarCostumeDetector.CollectCostumeRoots(dstRoot);
            // NOTE:
            // - 直近は MA 連携側の実装を開発中のため、スケール補正は
            //   OCTModularAvatarCostumeScaleAdjuster（MergeArmatureマッピング）に寄せています。
            // - そのため MA が無い環境では衣装スケール補正が行われないケースが一時的に発生し得ますが、
            //   現時点ではこの挙動を許容します（将来的に再検討予定）。
            // - 旧実装のスケール補正（OCTCostumeScaleAdjuster.AdjustCostumeScalesLegacy）は
            //   いったんレガシー扱いとしてここでは使用しません。
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
