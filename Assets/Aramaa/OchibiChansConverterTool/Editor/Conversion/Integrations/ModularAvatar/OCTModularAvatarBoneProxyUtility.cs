#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal static class OCTModularAvatarBoneProxyUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        private enum ValidationResult
        {
            Ok,
            TargetMissing,
            NotInAvatar
        }

        private sealed class ProxyInfo
        {
            public readonly Component Proxy;
            public readonly GameObject TargetSnapshot;
            public readonly Vector3 WorldPos;
            public readonly Quaternion WorldRot;
            public readonly string AttachmentModeName;

            public bool Applied;

            public ProxyInfo(Component proxy)
            {
                Proxy = proxy;
                var target = OCTModularAvatarReflection.GetBoneProxyTarget(proxy);
                TargetSnapshot = target != null ? target.gameObject : null;

                var tr = proxy.transform;
                WorldPos = tr.position;
                WorldRot = tr.rotation;
                AttachmentModeName = OCTModularAvatarReflection.GetBoneProxyAttachmentModeName(proxy);
                Applied = false;
            }

            public Transform ResolveTarget()
            {
                if (TargetSnapshot != null) return TargetSnapshot.transform;
                return OCTModularAvatarReflection.GetBoneProxyTarget(Proxy);
            }
        }

        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            OCTModularAvatarIntegrationGuard.AppendVersionWarningIfNeeded(logs);

            if (!OCTModularAvatarReflection.TryGetBoneProxyType(out var boneProxyType))
            {
                logs?.Add(L("Log.MaboneProxySkipped"));
                return;
            }

            var proxyComponents = OCTModularAvatarReflection
                .GetComponentsInChildren(avatarRoot, boneProxyType, includeInactive: true)
                .ToArray();

            if (proxyComponents.Length == 0)
            {
                logs?.Add(L("Log.MaboneProxyNone"));
                return;
            }

            logs?.Add(F("Log.MaboneProxyCount", proxyComponents.Length));

            var proxyInfos = proxyComponents
                .Where(p => p != null)
                .Select(p => new ProxyInfo(p))
                .ToList();

            var unpackedPrefabRoots = new HashSet<GameObject>();

            foreach (var info in proxyInfos)
            {
                ProcessProxy(avatarRoot, info, unpackedPrefabRoots, logs);
            }

            foreach (var info in proxyInfos
                         .Where(i => i != null && i.Applied)
                         .OrderBy(i => GetDepthFromRoot(avatarRoot.transform, i.Proxy.transform)))
            {
                AdjustTransform(info);
            }

            foreach (var info in proxyInfos)
            {
                if (info?.Proxy != null)
                {
                    Object.DestroyImmediate(info.Proxy);
                }
            }
        }

        private static void ProcessProxy(
            GameObject avatarRoot,
            ProxyInfo proxy,
            HashSet<GameObject> unpackedPrefabRoots,
            List<string> logs
        )
        {
            if (avatarRoot == null || proxy?.Proxy == null)
            {
                return;
            }

            var target = proxy.ResolveTarget();
            var validation = ValidateTarget(avatarRoot, target);

            if (validation == ValidationResult.Ok)
            {
                proxy.Applied = true;
                UnpackPrefabIfNeeded(proxy.Proxy.gameObject, unpackedPrefabRoots, logs);

                string suffix = string.Empty;
                int i = 1;
                while (target.Find(proxy.Proxy.gameObject.name + suffix) != null)
                {
                    suffix = $" ({i++})";
                }

                proxy.Proxy.gameObject.name += suffix;

                var proxyTransform = proxy.Proxy.transform;
                proxyTransform.SetParent(target, worldPositionStays: true);

                logs?.Add(F("Log.MaboneProxyProcessed", OCTConversionLogFormatter.GetHierarchyPath(proxyTransform)));
            }
            else
            {
                logs?.Add(F(
                    "Log.MaboneProxySkipDetail",
                    OCTConversionLogFormatter.GetHierarchyPath(proxy.Proxy.transform),
                    validation
                ));
            }
        }

        private static void AdjustTransform(ProxyInfo proxy)
        {
            if (proxy?.Proxy == null)
            {
                return;
            }

            bool keepPos;
            bool keepRot;

            switch (proxy.AttachmentModeName)
            {
                case "AsChildKeepWorldPose":
                    keepPos = true;
                    keepRot = true;
                    break;
                case "AsChildKeepPosition":
                    keepPos = true;
                    keepRot = false;
                    break;
                case "AsChildKeepRotation":
                    keepPos = false;
                    keepRot = true;
                    break;
                case "Unset":
                case "AsChildAtRoot":
                default:
                    keepPos = false;
                    keepRot = false;
                    break;
            }

            var t = proxy.Proxy.transform;
            if (keepPos)
            {
                t.position = proxy.WorldPos;
            }
            else
            {
                t.localPosition = Vector3.zero;
            }

            if (keepRot)
            {
                t.rotation = proxy.WorldRot;
            }
            else
            {
                t.localRotation = Quaternion.identity;
            }
        }

        private static ValidationResult ValidateTarget(GameObject avatarRoot, Transform proxyTarget)
        {
            if (avatarRoot == null)
            {
                return ValidationResult.NotInAvatar;
            }

            if (proxyTarget == null)
            {
                return ValidationResult.TargetMissing;
            }

            var avatar = avatarRoot.transform;
            var node = proxyTarget;

            while (node != null && node != avatar)
            {
                node = node.parent;
            }

            return node == null ? ValidationResult.NotInAvatar : ValidationResult.Ok;
        }

        private static int GetDepthFromRoot(Transform root, Transform node)
        {
            if (root == null || node == null)
            {
                return int.MaxValue;
            }

            int depth = 0;
            var cur = node;
            while (cur != null && cur != root)
            {
                depth++;
                cur = cur.parent;
            }

            return cur == null ? int.MaxValue : depth;
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

            logs?.Add(F("Log.MaboneProxyPrefabUnpacked", OCTConversionLogFormatter.GetHierarchyPath(instanceRoot.transform)));
            PrefabUtility.UnpackPrefabInstance(instanceRoot, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }
    }
}
#endif
