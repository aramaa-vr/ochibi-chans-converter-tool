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
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);
        private static bool _hadReflectionAccessFailure;

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
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch (Exception e)
                {
                    WarnOnce($"FindTypeInLoadedAssemblies:{asm.FullName}:{fullName}",
                        $"[OchibiChansConverterTool] Failed to scan assembly '{asm.FullName}' for type '{fullName}'. This warning is shown once per assembly/type. Error: {e.Message}");
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
                var prop = t.GetProperty("target", BindingFlags.Instance | BindingFlags.Public);
                return prop?.GetValue(boneProxy) as Transform;
            }
            catch (Exception e)
            {
                WarnOnce($"GetBoneProxyTarget:{t.FullName}",
                    $"[OchibiChansConverterTool] Failed to read BoneProxy.target via reflection from '{t.FullName}'. This warning is shown once per type. Error: {e.Message}");
                return null;
            }
        }

        internal static string GetBoneProxyAttachmentModeName(Component boneProxy)
        {
            if (boneProxy == null) return null;

            var t = boneProxy.GetType();
            try
            {
                var field = t.GetField("attachmentMode", BindingFlags.Instance | BindingFlags.Public);
                var value = field?.GetValue(boneProxy);
                return value?.ToString();
            }
            catch (Exception e)
            {
                WarnOnce($"GetBoneProxyAttachmentModeName:{t.FullName}",
                    $"[OchibiChansConverterTool] Failed to read BoneProxy.attachmentMode via reflection from '{t.FullName}'. This warning is shown once per type. Error: {e.Message}");
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
            catch (Exception e)
            {
                var typeName = mergeArmature.GetType().FullName;
                WarnOnce($"InvokeGetBonesMapping:{typeName}",
                    $"[OchibiChansConverterTool] Failed to invoke GetBonesMapping via reflection from '{typeName}'. This warning is shown once per type. Error: {e.Message}");
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
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                {
                    transform = field.GetValue(tuple) as Transform;
                    return transform != null;
                }

                var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
                if (prop != null)
                {
                    transform = prop.GetValue(tuple) as Transform;
                    return transform != null;
                }
            }
            catch (Exception e)
            {
                WarnOnce($"TryGetValueTupleItem:{type.FullName}:{memberName}",
                    $"[OchibiChansConverterTool] Failed to read tuple member '{memberName}' from '{type.FullName}'. This warning is shown once per type/member. Error: {e.Message}");
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
            catch (Exception e)
            {
                WarnOnce($"GetComponentsInChildren:{root.name}:{componentType.FullName}",
                    $"[OchibiChansConverterTool] Failed to get components of type '{componentType.FullName}' under '{root.name}'. This warning is shown once per root/type. Error: {e.Message}");
                return Enumerable.Empty<Component>();
            }
        }

        internal static void ResetReflectionFailureFlag()
        {
            _hadReflectionAccessFailure = false;
        }

        internal static bool ConsumeReflectionFailureFlag()
        {
            var hadFailure = _hadReflectionAccessFailure;
            _hadReflectionAccessFailure = false;
            return hadFailure;
        }

        private static void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message)) return;
            _hadReflectionAccessFailure = true;
            if (!WarnedKeys.Add(key)) return;
            Debug.LogWarning(message);
        }
    }
}
#endif
