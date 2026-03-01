#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - MA BoneProxy を「疑似実行」して、複製先アバター上で親子付けと姿勢補正を行います。
// - MA 直接参照は行わず、反射経由で target / attachmentMode を取得します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 処理順は次の通りです。
//   1) Proxy 情報を事前スナップショット
//   2) 親子付け（必要時のみ prefab unpack）
//   3) 親→子順で keep-world 系補正
//   4) BoneProxy コンポーネント削除
// - target が見つからない場合は TargetMissing としてスキップログを残します。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - BoneProxy 実装差分に追従する時は、Reflection ヘルパとこの補正順をセットで見直す。
// - validation 結果はログに出る文字列になるため、enum 名変更時は翻訳影響も確認する。
// - ここでは「処理を止める」より「安全に継続」を優先する。
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA BoneProxy の疑似適用ユーティリティです。
    /// </summary>
    internal static class OCTModularAvatarBoneProxyUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);
        private static readonly HashSet<string> WarnedAttachmentModeNames = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>
        /// BoneProxy target の妥当性判定結果。
        /// </summary>
        private enum ValidationResult
        {
            Ok,
            TargetMissing,
            NotInAvatar
        }

        /// <summary>
        /// 1 BoneProxy 分のスナップショット情報。
        /// </summary>
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

            /// <summary>
            /// 先に取ったスナップショットを優先して target を返します。
            /// </summary>
            public Transform ResolveTarget()
            {
                if (TargetSnapshot != null) return TargetSnapshot.transform;
                return OCTModularAvatarReflection.GetBoneProxyTarget(Proxy);
            }
        }

        /// <summary>
        /// avatarRoot 配下の BoneProxy を収集・疑似適用します。
        /// </summary>
        public static void ProcessBoneProxies(GameObject avatarRoot, List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return;
            }

            // バージョン警告は出すが処理は続行
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

            // 1) 親子付け
            foreach (var info in proxyInfos)
            {
                ProcessProxy(avatarRoot, info, unpackedPrefabRoots, logs);
            }

            // 2) keep-world 補正（親->子）
            foreach (var info in proxyInfos
                         .Where(i => i != null && i.Applied)
                         .OrderBy(i => GetDepthFromRoot(avatarRoot.transform, i.Proxy.transform)))
            {
                AdjustTransform(info);
            }

            // 3) BoneProxy コンポーネント削除
            foreach (var info in proxyInfos)
            {
                if (info?.Proxy != null)
                {
                    Object.DestroyImmediate(info.Proxy);
                }
            }
        }

        /// <summary>
        /// 1 つの Proxy に対し、target 検証後に親子付けを行います。
        /// </summary>
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

        /// <summary>
        /// attachmentMode に応じて位置・回転を補正します。
        /// </summary>
        private static void AdjustTransform(ProxyInfo proxy)
        {
            if (proxy?.Proxy == null)
            {
                return;
            }

            var attachmentMode = ParseAttachmentMode(proxy.AttachmentModeName);
            bool keepPos = attachmentMode == AttachmentModeBehavior.KeepWorldPose
                           || attachmentMode == AttachmentModeBehavior.KeepPosition;
            bool keepRot = attachmentMode == AttachmentModeBehavior.KeepWorldPose
                           || attachmentMode == AttachmentModeBehavior.KeepRotation;

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

        private enum AttachmentModeBehavior
        {
            AtRoot,
            KeepWorldPose,
            KeepPosition,
            KeepRotation
        }

        /// <summary>
        /// attachmentMode 名を安全に解釈します。
        /// MA 側で命名が多少変わっても "Keep*" キーワードから解釈を試みます。
        /// </summary>
        private static AttachmentModeBehavior ParseAttachmentMode(string attachmentModeName)
        {
            if (string.IsNullOrEmpty(attachmentModeName))
            {
                return AttachmentModeBehavior.AtRoot;
            }

            if (string.Equals(attachmentModeName, "AsChildKeepWorldPose", System.StringComparison.Ordinal)
                || attachmentModeName.IndexOf("KeepWorldPose", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AttachmentModeBehavior.KeepWorldPose;
            }

            if (string.Equals(attachmentModeName, "AsChildKeepPosition", System.StringComparison.Ordinal)
                || attachmentModeName.IndexOf("KeepPosition", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AttachmentModeBehavior.KeepPosition;
            }

            if (string.Equals(attachmentModeName, "AsChildKeepRotation", System.StringComparison.Ordinal)
                || attachmentModeName.IndexOf("KeepRotation", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AttachmentModeBehavior.KeepRotation;
            }

            if (!string.Equals(attachmentModeName, "Unset", System.StringComparison.Ordinal)
                && !string.Equals(attachmentModeName, "AsChildAtRoot", System.StringComparison.Ordinal))
            {
                WarnUnknownAttachmentModeOnce(attachmentModeName);
            }

            return AttachmentModeBehavior.AtRoot;
        }

        private static void WarnUnknownAttachmentModeOnce(string attachmentModeName)
        {
            if (string.IsNullOrEmpty(attachmentModeName))
            {
                return;
            }

            if (!WarnedAttachmentModeNames.Add(attachmentModeName))
            {
                return;
            }

            Debug.LogWarning($"[OchibiChansConverterTool] Unknown BoneProxy attachment mode '{attachmentModeName}'. Fallback: AsChildAtRoot.");
        }

        /// <summary>
        /// target が avatarRoot 配下かどうかを判定します。
        /// </summary>
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

        /// <summary>
        /// root から node までの深さを返します（root 外なら MaxValue）。
        /// </summary>
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

        /// <summary>
        /// Proxy の prefab instance を必要時のみ unpack します（1 instance root につき 1 回）。
        /// </summary>
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
