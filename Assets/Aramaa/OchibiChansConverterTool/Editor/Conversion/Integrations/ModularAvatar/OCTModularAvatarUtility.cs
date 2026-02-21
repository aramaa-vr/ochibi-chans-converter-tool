#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTModularAvatarUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar 依存処理の「入口」を1箇所に集約するユーティリティです。
// - CHIBI_MODULAR_AVATAR の有無判定と、各専用クラスへの委譲を担当します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - このクラスは「依存有無の判断」と「処理の振り分け」だけを担当します。
// - 実処理は以下へ分離されています。
//   - MA依存: OCTModularAvatarBoneProxyUtility / OCTModularAvatarCostumeDetector
//   - MA非依存: OCTCostumeScaleAdjuster / OCTCostumeBlendShapeAdjuster
// - MA未導入時に落とさない（安全スキップ）ことを最優先にしています。
// - 処理順は「衣装スケール補正 -> BlendShape 同期」の順で固定です。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MA依存の #if 分岐は可能な限りこのクラスへ集約する。
// - 呼び出し元（Pipeline）には、業務フローのみを残し、実装詳細を漏らさない。
// - MA未導入時のログ文言（スキップ/不足）は既存キーを使って互換維持する。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// Modular Avatar 関連処理のエントリポイント。
    /// CHIBI_MODULAR_AVATAR の有無による分岐をここに集約する。
    /// </summary>
    internal static class OCTModularAvatarUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);

        internal static bool IsModularAvatarAvailable
        {
            get
            {
#if CHIBI_MODULAR_AVATAR
                return true;
#else
                return false;
#endif
            }
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

#if CHIBI_MODULAR_AVATAR
            OCTModularAvatarBoneProxyUtility.ProcessBoneProxies(avatarRoot, logs);
#endif
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
                logs?.Add(L("Log.ModularAvatarMissing"));
                return true;
            }

            var costumeRoots = OCTModularAvatarCostumeDetector.CollectCostumeRoots(dstRoot);
            if (!OCTCostumeScaleAdjuster.AdjustCostumeScales(
                dstRoot,
                basePrefabRoot,
                costumeRoots,
                logs
            ))
            {
                return false;
            }

            return OCTCostumeBlendShapeAdjuster.AdjustCostumeBlendShapes(
                basePrefabRoot,
                costumeRoots,
                logs
            );
        }
    }
}
#endif
