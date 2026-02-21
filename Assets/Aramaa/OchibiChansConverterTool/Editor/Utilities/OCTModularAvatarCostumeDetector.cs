#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// MA Mesh Settings を起点に衣装ルート候補を抽出する責務を持つクラス。
    /// </summary>
    internal static class OCTModularAvatarCostumeDetector
    {
        internal static List<Transform> CollectCostumeRoots(GameObject dstRoot)
        {
            var costumeRoots = new List<Transform>();
            if (dstRoot == null)
            {
                return costumeRoots;
            }

#if CHIBI_MODULAR_AVATAR
            costumeRoots = dstRoot.GetComponentsInChildren<ModularAvatarMeshSettings>(true)
                .Select(c => c != null ? c.transform : null)
                .Where(t => t != null && t.gameObject != dstRoot)
                .Distinct()
                .ToList();
#endif

            return costumeRoots;
        }
    }
}
#endif
