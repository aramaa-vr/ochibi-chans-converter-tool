#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal static class OCTModularAvatarUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);

        internal static bool IsModularAvatarAvailable
        {
            get { return OCTModularAvatarIntegrationGuard.IsModularAvatarDetected(); }
        }

        internal static void AppendBoneProxySkippedLog(List<string> logs)
        {
            if (logs == null) return;
            logs.Add(L("Log.MaboneProxySkipped"));
        }

        internal static void AppendModularAvatarSkippedLog(List<string> logs)
        {
            if (logs == null) return;
            logs.Add(L("Log.ModularAvatarMissing"));
        }

        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            OCTModularAvatarBoneProxyUtility.ProcessBoneProxies(avatarRoot, logs);
        }

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
