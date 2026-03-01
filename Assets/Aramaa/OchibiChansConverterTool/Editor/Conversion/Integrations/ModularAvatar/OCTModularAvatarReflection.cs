#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar へのコンパイル時依存を避けるためのリフレクションヘルパ。
    /// </summary>
    internal static class OCTModularAvatarReflection
    {
        internal const string BoneProxyTypeName = "nadena.dev.modular_avatar.core.ModularAvatarBoneProxy";
        internal const string MergeArmatureTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature";
        internal const string MeshSettingsTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMeshSettings";

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        internal static bool TryGetBoneProxyType(out Type type) => TryGetType(BoneProxyTypeName, out type);
        internal static bool TryGetMergeArmatureType(out Type type) => TryGetType(MergeArmatureTypeName, out type);
        internal static bool TryGetMeshSettingsType(out Type type) => TryGetType(MeshSettingsTypeName, out type);

        internal static bool TryGetType(string fullName, out Type type)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                type = null;
                return false;
            }

            if (TypeCache.TryGetValue(fullName, out type))
            {
                return type != null;
            }

            type = FindTypeInLoadedAssemblies(fullName);
            TypeCache[fullName] = type;
            return type != null;
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            // Type.GetType は assembly-qualified を要求するケースが多いので、ロード済みアセンブリから探索する
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch
                {
                    // 例外は無視して継続
                }
            }

            return null;
        }

        internal static Transform GetBoneProxyTarget(Component boneProxy)
        {
            if (boneProxy == null) return null;

            var t = boneProxy.GetType();
            try
            {
                // public Transform target { get; set; }
                var prop = t.GetProperty("target", BindingFlags.Instance | BindingFlags.Public);
                return prop?.GetValue(boneProxy) as Transform;
            }
            catch
            {
                return null;
            }
        }

        internal static string GetBoneProxyAttachmentModeName(Component boneProxy)
        {
            if (boneProxy == null) return null;

            var t = boneProxy.GetType();
            try
            {
                // public BoneProxyAttachmentMode attachmentMode
                var field = t.GetField("attachmentMode", BindingFlags.Instance | BindingFlags.Public);
                var value = field?.GetValue(boneProxy);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        internal static object InvokeGetBonesMapping(Component mergeArmature)
        {
            if (mergeArmature == null) return null;
            try
            {
                var method = mergeArmature.GetType().GetMethod("GetBonesMapping", BindingFlags.Instance | BindingFlags.Public);
                return method?.Invoke(mergeArmature, null);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryGetValueTupleItem(object tuple, string memberName, out Transform transform)
        {
            transform = null;
            if (tuple == null || string.IsNullOrEmpty(memberName)) return false;

            var type = tuple.GetType();
            try
            {
                // ValueTuple は field が基本
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                {
                    transform = field.GetValue(tuple) as Transform;
                    return transform != null;
                }

                // 念のため property も見る
                var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
                if (prop != null)
                {
                    transform = prop.GetValue(tuple) as Transform;
                    return transform != null;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        internal static IEnumerable<Component> GetComponentsInChildren(GameObject root, Type componentType, bool includeInactive)
        {
            if (root == null || componentType == null) return Enumerable.Empty<Component>();

            try
            {
                return root.GetComponentsInChildren(componentType, includeInactive)
                    .OfType<Component>()
                    .Where(c => c != null);
            }
            catch
            {
                return Enumerable.Empty<Component>();
            }
        }
    }
}
#endif
