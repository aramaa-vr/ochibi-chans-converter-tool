#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal static class OCTModularAvatarCostumeDetector
    {
        internal static List<Transform> CollectCostumeRoots(GameObject dstRoot)
        {
            var costumeRoots = new List<Transform>();
            if (dstRoot == null)
            {
                return costumeRoots;
            }

            if (!OCTModularAvatarReflection.TryGetMeshSettingsType(out var meshSettingsType))
            {
                return costumeRoots;
            }

            costumeRoots = OCTModularAvatarReflection
                .GetComponentsInChildren(dstRoot, meshSettingsType, includeInactive: true)
                .Select(c => c != null ? c.transform : null)
                .Where(t => t != null && t.gameObject != dstRoot)
                .Distinct()
                .ToList();

            return costumeRoots;
        }
    }
}
#endif
