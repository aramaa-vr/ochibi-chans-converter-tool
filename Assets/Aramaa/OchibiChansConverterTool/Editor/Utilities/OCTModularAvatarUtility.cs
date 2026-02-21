#if UNITY_EDITOR
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
        /// MABoneProxy を処理します。Modular Avatar 未導入時はスキップログのみ残します。
        /// </summary>
        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            if (!IsModularAvatarAvailable)
            {
                logs?.Add(L("Log.MaboneProxySkipped"));
                return;
            }

#if CHIBI_MODULAR_AVATAR
            OCTModularAvatarBoneProxyUtility.ProcessBoneProxies(avatarRoot, logs);
#endif
        }

        /// <summary>
        /// MA Mesh Settings が付与された衣装ルートを検出し、衣装スケール調整を行います。
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
            return OCTModularAvatarCostumeScaleAdjuster.AdjustCostumeScales(
                dstRoot,
                basePrefabRoot,
                costumeRoots,
                logs
            );
        }
    }
}
#endif
