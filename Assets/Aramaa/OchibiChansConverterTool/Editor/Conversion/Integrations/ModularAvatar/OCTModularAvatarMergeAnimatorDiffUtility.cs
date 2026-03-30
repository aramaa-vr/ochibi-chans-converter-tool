#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// ModularAvatarMergeAnimator の参照差分の適用/復元を扱います。
    /// </summary>
    internal static class OCTModularAvatarMergeAnimatorDiffUtility
    {
        private const int JsonVersion = 1;

        [Serializable]
        private sealed class MergeAnimatorDiffJson
        {
            public int version;
            public List<MergeAnimatorDiffItem> items = new List<MergeAnimatorDiffItem>();
        }

        [Serializable]
        private sealed class MergeAnimatorDiffItem
        {
            public string objectFullPath;
            public string sourceGuid;
            public string targetGuid;
        }

        private sealed class MergeAnimatorEntry
        {
            public Component Component;
            public string AnimatorGuid;
            public UnityEngine.Object AnimatorAsset;
        }

        private sealed class MergeAnimatorAccessor
        {
            public readonly Func<Component, UnityEngine.Object> Getter;
            public readonly Action<Component, UnityEngine.Object> Setter;

            public MergeAnimatorAccessor(Func<Component, UnityEngine.Object> getter, Action<Component, UnityEngine.Object> setter)
            {
                Getter = getter;
                Setter = setter;
            }
        }

        public static void ApplyChibiSideAnimatorRefsAndStoreDiffs(
            string sourceChibiPrefabPath,
            GameObject sourceAvatarRoot,
            GameObject sourceChibiRoot,
            GameObject dstRoot,
            List<string> logs = null)
        {
            if (!OCTModularAvatarUtility.IsModularAvatarAvailable || sourceAvatarRoot == null || sourceChibiRoot == null || dstRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

            var sourceMap = BuildMergeAnimatorMap(sourceAvatarRoot);
            var chibiMap = BuildMergeAnimatorMap(sourceChibiRoot);
            var dstMap = BuildMergeAnimatorMap(dstRoot);

            var diffItems = new List<MergeAnimatorDiffItem>();
            foreach (var kv in chibiMap)
            {
                var path = kv.Key;
                var chibiEntry = kv.Value;
                if (chibiEntry == null || string.IsNullOrEmpty(chibiEntry.AnimatorGuid))
                {
                    continue;
                }

                if (!sourceMap.TryGetValue(path, out var sourceEntry) || sourceEntry == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(sourceEntry.AnimatorGuid) || string.Equals(sourceEntry.AnimatorGuid, chibiEntry.AnimatorGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!dstMap.TryGetValue(path, out var dstEntry) || dstEntry == null)
                {
                    continue;
                }

                Undo.RecordObject(dstEntry.Component, "Apply MergeAnimator Ref");
                if (!TrySetAnimatorAsset(dstEntry.Component, chibiEntry.AnimatorAsset))
                {
                    continue;
                }
                EditorUtility.SetDirty(dstEntry.Component);

                diffItems.Add(new MergeAnimatorDiffItem
                {
                    objectFullPath = path,
                    sourceGuid = sourceEntry.AnimatorGuid,
                    targetGuid = chibiEntry.AnimatorGuid
                });
            }

            var sourceAvatarPrefabPath = ResolvePrefabAssetPath(sourceAvatarRoot);
            if (!string.IsNullOrEmpty(sourceChibiPrefabPath) &&
                !string.IsNullOrEmpty(sourceAvatarPrefabPath))
            {
                var payload = new MergeAnimatorDiffJson
                {
                    version = JsonVersion,
                    items = diffItems ?? new List<MergeAnimatorDiffItem>()
                };
                OCTPrefabDropdownCache.SaveMergeAnimatorDiffJson(
                    sourceChibiPrefabPath,
                    sourceAvatarPrefabPath,
                    JsonUtility.ToJson(payload, true));
            }

            logs.Add($"[MA MergeAnimator Diff] Applied: {diffItems.Count}");
        }

        public static void RestoreAnimatorRefsFromStoredDiff(
            string originalAvatarPrefabPath,
            GameObject avatarRoot,
            List<string> logs = null)
        {
            if (!OCTModularAvatarUtility.IsModularAvatarAvailable || avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

            var chibiPrefabPath = ResolvePrefabAssetPath(avatarRoot);
            if (string.IsNullOrEmpty(chibiPrefabPath) || string.IsNullOrEmpty(originalAvatarPrefabPath))
            {
                return;
            }

            if (!OCTPrefabDropdownCache.TryGetMergeAnimatorDiffJson(chibiPrefabPath, originalAvatarPrefabPath, out var storedJson)
                || string.IsNullOrWhiteSpace(storedJson))
            {
                return;
            }

            MergeAnimatorDiffJson parsed;
            try
            {
                parsed = JsonUtility.FromJson<MergeAnimatorDiffJson>(storedJson);
            }
            catch
            {
                logs.Add("[MA MergeAnimator Diff] Restore skipped (metadata parse failed).");
                return;
            }

            if (parsed?.items == null || parsed.items.Count == 0)
            {
                return;
            }

            var dstMap = BuildMergeAnimatorMap(avatarRoot);
            foreach (var item in parsed.items)
            {
                if (item == null || string.IsNullOrEmpty(item.objectFullPath) || string.IsNullOrEmpty(item.sourceGuid))
                {
                    continue;
                }

                if (!dstMap.TryGetValue(item.objectFullPath, out var dstEntry) || dstEntry == null)
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: path not found: {item.objectFullPath}");
                    continue;
                }

                var sourceAssetPath = AssetDatabase.GUIDToAssetPath(item.sourceGuid);
                if (string.IsNullOrEmpty(sourceAssetPath))
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: GUID not resolved: {item.sourceGuid}");
                    continue;
                }

                var sourceAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sourceAssetPath);
                if (sourceAsset == null)
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: asset missing: {sourceAssetPath}");
                    continue;
                }

                Undo.RecordObject(dstEntry.Component, "Restore MergeAnimator Ref");
                if (!TrySetAnimatorAsset(dstEntry.Component, sourceAsset))
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: animator property not found: {item.objectFullPath}");
                    continue;
                }
                EditorUtility.SetDirty(dstEntry.Component);
            }
        }

        private static Dictionary<string, MergeAnimatorEntry> BuildMergeAnimatorMap(GameObject root)
        {
            var map = new Dictionary<string, MergeAnimatorEntry>(StringComparer.Ordinal);
            if (root == null)
            {
                return map;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var transform in transforms)
            {
                if (transform == null)
                {
                    continue;
                }

                var components = transform.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (!IsMergeAnimatorComponent(component))
                    {
                        continue;
                    }

                    if (!TryGetAnimatorAsset(component, out var animatorAsset, out var animatorGuid))
                    {
                        continue;
                    }

                    var objectPath = BuildObjectFullPath(root.transform, transform);
                    map[objectPath] = new MergeAnimatorEntry
                    {
                        Component = component,
                        AnimatorGuid = animatorGuid,
                        AnimatorAsset = animatorAsset
                    };
                }
            }

            return map;
        }

        private static bool IsMergeAnimatorComponent(Component component)
        {
            return component != null
                   && string.Equals(component.GetType().Name, "ModularAvatarMergeAnimator", StringComparison.Ordinal);
        }

        private static string BuildObjectFullPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            var rel = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(rel) ? "/" : rel;
        }

        private static string ResolvePrefabAssetPath(GameObject root)
        {
            if (root == null)
            {
                return string.Empty;
            }

            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(root);
            if (instanceRoot != null)
            {
                var instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                if (!string.IsNullOrEmpty(instancePath))
                {
                    return instancePath;
                }
            }

            if (EditorUtility.IsPersistent(root))
            {
                return AssetDatabase.GetAssetPath(root);
            }

            return string.Empty;
        }

        private static bool TryGetAnimatorAsset(Component component, out UnityEngine.Object asset, out string guid)
        {
            asset = null;
            guid = string.Empty;

            if (component == null)
            {
                return false;
            }

            if (!TryResolveAccessor(component.GetType(), out var accessor))
            {
                return false;
            }

            asset = accessor.Getter(component);
            if (asset == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid);
        }

        private static bool TrySetAnimatorAsset(Component component, UnityEngine.Object asset)
        {
            if (component == null || asset == null)
            {
                return false;
            }

            if (!TryResolveAccessor(component.GetType(), out var accessor))
            {
                return false;
            }

            accessor.Setter(component, asset);
            return true;
        }

        private static readonly Dictionary<Type, MergeAnimatorAccessor> AccessorCache = new Dictionary<Type, MergeAnimatorAccessor>();

        private static bool TryResolveAccessor(Type componentType, out MergeAnimatorAccessor accessor)
        {
            accessor = null;
            if (componentType == null)
            {
                return false;
            }

            if (AccessorCache.TryGetValue(componentType, out accessor))
            {
                return accessor != null;
            }

            var field = componentType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)
                                     && (string.Equals(f.Name, "animator", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(f.Name, "animatorController", StringComparison.OrdinalIgnoreCase)));

            if (field != null)
            {
                accessor = new MergeAnimatorAccessor(
                    getter: component => field.GetValue(component) as UnityEngine.Object,
                    setter: (component, value) => field.SetValue(component, value));
                AccessorCache[componentType] = accessor;
                return true;
            }

            var property = componentType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(p => p.CanRead
                                     && p.CanWrite
                                     && p.GetIndexParameters().Length == 0
                                     && typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType)
                                     && (string.Equals(p.Name, "animator", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(p.Name, "animatorController", StringComparison.OrdinalIgnoreCase)));

            if (property != null)
            {
                accessor = new MergeAnimatorAccessor(
                    getter: component => property.GetValue(component, null) as UnityEngine.Object,
                    setter: (component, value) => property.SetValue(component, value, null));
                AccessorCache[componentType] = accessor;
                return true;
            }

            AccessorCache[componentType] = null;
            return false;
        }
    }
}
#endif
