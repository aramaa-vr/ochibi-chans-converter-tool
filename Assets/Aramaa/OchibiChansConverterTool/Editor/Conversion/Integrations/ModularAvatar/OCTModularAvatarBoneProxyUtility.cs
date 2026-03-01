#if UNITY_EDITOR
//
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar の BoneProxy を複製先に対して「疑似実行」します。
// - MA 1.16.2 の BoneProxyProcessor（Editor 側）に近い処理順（事前スナップショット -> 親子順補正）で
//   keep-world 系の挙動が壊れにくいようにしています。
// - MA へのコンパイル時依存を避けるため、型参照はリフレクションで行います。
//
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar の BoneProxy を複製先へ適用するユーティリティです。
    /// </summary>
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

            public ProxyInfo(Component proxy)
            {
                Proxy = proxy;

                var target = OCTModularAvatarReflection.GetBoneProxyTarget(proxy);
                TargetSnapshot = target != null ? target.gameObject : null;

                var tr = proxy.transform;
                WorldPos = tr.position;
                WorldRot = tr.rotation;
                AttachmentModeName = OCTModularAvatarReflection.GetBoneProxyAttachmentModeName(proxy);
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

            // バージョン不一致は「警告のみ」（動作は試みる）
            OCTModularAvatarIntegrationGuard.AppendVersionWarningIfNeeded(logs);

            if (OCTModularAvatarIntegrationGuard.IsIntegrationDisabled)
            {
                logs?.Add(L("Log.MaboneProxySkippedDisabled"));
                return;
            }

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

            // 1) 事前スナップショット（target / world pose）
            var proxyInfos = proxyComponents
                .Where(p => p != null)
                .Select(p => new ProxyInfo(p))
                .ToList();

            // 2) Prefab unpack（必要な場合のみ）
            // ※ターゲット不正でスキップされる BoneProxy では Unpack しない。
            //   実際に移動するタイミング（ProcessProxy 内）でのみ Unpack する。
            var unpackedPrefabRoots = new HashSet<GameObject>();

            // 3) まずは親子関係だけを確定させる（keep-world は後段で調整）
            foreach (var info in proxyInfos)
            {
                ProcessProxy(avatarRoot, info, unpackedPrefabRoots, logs);
            }

            // 4) 親->子 の順で keep-world の補正を行う（MA 1.16.2 に寄せる）
            foreach (var info in proxyInfos
                         .OrderBy(i => GetDepthFromRoot(avatarRoot.transform, i.Proxy.transform)))
            {
                AdjustTransform(info);
            }

            // 5) BoneProxy コンポーネントを削除
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
                // 実際に移動するケースだけ Prefab を Unpack する
                // （ターゲット不正で最終的にスキップされる BoneProxy では Unpack しない）
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
                // MA BoneProxyAttachmentMode
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
