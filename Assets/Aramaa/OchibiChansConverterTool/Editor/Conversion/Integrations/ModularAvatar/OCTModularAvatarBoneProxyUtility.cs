#if UNITY_EDITOR && CHIBI_MODULAR_AVATAR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTModularAvatarBoneProxyUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - MABoneProxy（Modular Avatar）を複製先に対して実行します
// - Modular Avatar の BoneProxyProcessor に近い処理を行います
//
// ============================================================================

using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// Modular Avatar の MABoneProxy を複製先へ適用するユーティリティです。
    /// </summary>
    internal static class OCTModularAvatarBoneProxyUtility
    {
        private enum ValidationResult
        {
            Ok,
            MovingTarget,
            NotInAvatar
        }

        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var proxies = avatarRoot.GetComponentsInChildren<ModularAvatarBoneProxy>(true);
            if (proxies == null || proxies.Length == 0)
            {
                logs?.Add(OCTLocalizationService.Get("Log.MaboneProxyNone"));
                return;
            }

            logs?.Add(OCTLocalizationService.Format("Log.MaboneProxyCount", proxies.Length));

            var unpackedPrefabRoots = new HashSet<GameObject>();

            foreach (var proxy in proxies)
            {
                if (proxy == null)
                {
                    continue;
                }

                ProcessProxy(avatarRoot, proxy, unpackedPrefabRoots, logs);
            }
        }

        private static void ProcessProxy(
            GameObject avatarRoot,
            ModularAvatarBoneProxy proxy,
            HashSet<GameObject> unpackedPrefabRoots,
            List<string> logs
        )
        {
            var target = proxy.target;
            var validation = target != null ? ValidateTarget(avatarRoot, target) : ValidationResult.NotInAvatar;

            if (target != null && validation == ValidationResult.Ok)
            {
                UnpackPrefabIfNeeded(proxy.gameObject, unpackedPrefabRoots, logs);

                string suffix = string.Empty;
                int i = 1;
                while (target.Find(proxy.gameObject.name + suffix) != null)
                {
                    suffix = $" ({i++})";
                }

                var proxyTransform = proxy.transform;
                proxy.gameObject.name += suffix;

                proxyTransform.SetParent(target, worldPositionStays: true);

                bool keepPos;
                bool keepRot;
                switch (proxy.attachmentMode)
                {
                    default:
                    case BoneProxyAttachmentMode.Unset:
                    case BoneProxyAttachmentMode.AsChildAtRoot:
                        keepPos = false;
                        keepRot = false;
                        break;
                    case BoneProxyAttachmentMode.AsChildKeepWorldPose:
                        keepPos = true;
                        keepRot = true;
                        break;
                    case BoneProxyAttachmentMode.AsChildKeepPosition:
                        keepPos = true;
                        keepRot = false;
                        break;
                    case BoneProxyAttachmentMode.AsChildKeepRotation:
                        keepPos = false;
                        keepRot = true;
                        break;
                }

                if (!keepPos)
                {
                    proxyTransform.localPosition = Vector3.zero;
                }

                if (!keepRot)
                {
                    proxyTransform.localRotation = Quaternion.identity;
                }

                logs?.Add(OCTLocalizationService.Format("Log.MaboneProxyProcessed", OCTConversionLogFormatter.GetHierarchyPath(proxyTransform)));
            }
            else
            {
                logs?.Add(OCTLocalizationService.Format("Log.MaboneProxySkipDetail", OCTConversionLogFormatter.GetHierarchyPath(proxy.transform), validation));
            }

            Object.DestroyImmediate(proxy);
        }

        private static ValidationResult ValidateTarget(GameObject avatarRoot, Transform proxyTarget)
        {
            if (avatarRoot == null || proxyTarget == null)
            {
                return ValidationResult.NotInAvatar;
            }

            var avatar = avatarRoot.transform;
            var node = proxyTarget;

            while (node != null && node != avatar)
            {
                if (node.GetComponent<ModularAvatarMergeArmature>() != null ||
                    node.GetComponent<ModularAvatarBoneProxy>() != null)
                {
                    return ValidationResult.MovingTarget;
                }

                node = node.parent;
            }

            return node == null ? ValidationResult.NotInAvatar : ValidationResult.Ok;
        }

        private static void UnpackPrefabIfNeeded(
            GameObject proxyObject,
            HashSet<GameObject> unpackedPrefabRoots,
            List<string> logs
        )
        {
            if (proxyObject == null)
            {
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(proxyObject))
            {
                return;
            }

            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(proxyObject);
            if (instanceRoot == null)
            {
                return;
            }

            if (!unpackedPrefabRoots.Add(instanceRoot))
            {
                return;
            }

            logs?.Add(OCTLocalizationService.Format("Log.MaboneProxyPrefabUnpacked", OCTConversionLogFormatter.GetHierarchyPath(instanceRoot.transform)));
            PrefabUtility.UnpackPrefabInstance(instanceRoot, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }
    }
}
#endif
