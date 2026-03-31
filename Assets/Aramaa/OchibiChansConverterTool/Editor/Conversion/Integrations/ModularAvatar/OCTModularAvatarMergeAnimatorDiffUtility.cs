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
        [Serializable]
        private sealed class MergeAnimatorDiffJson
        {
            // フォーマット互換は FaceMeshCache ファイル名（v11 など）側で管理する。
            public List<MergeAnimatorDiffItem> items = new List<MergeAnimatorDiffItem>();
        }

        [Serializable]
        private sealed class MergeAnimatorDiffItem
        {
            public string objectFullPath;
            public int componentIndex;
            public string sourceGuid;
            public string targetGuid;
        }

        private sealed class MergeAnimatorEntry
        {
            public Component Component;
            public int ComponentIndex;
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

        /// <summary>
        /// 通常変換（元 -> おちび）時に、
        /// MergeAnimator の参照差分を適用し、差分JSONを FaceMeshCache へ保存します。
        /// </summary>
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
            // 設計方針:
            // - 本ツールは「おおよそ一致していれば調整する」ことを目的とする。
            // - すべてのユーザー編集パターン（大規模な名前変更/再配置）への追従は目指さない。
            // - 複雑なフォールバック照合は入れず、パス + componentIndex の単純一致を優先する。
            // 同一パス同士で比較し、GUID が異なるものだけ差分として扱う。
            foreach (var kv in chibiMap)
            {
                var path = kv.Key;
                var chibiEntries = kv.Value;
                if (chibiEntries == null)
                {
                    continue;
                }

                if (!sourceMap.TryGetValue(path, out var sourceEntries) || sourceEntries == null)
                {
                    continue;
                }

                if (!dstMap.TryGetValue(path, out var dstEntries) || dstEntries == null)
                {
                    continue;
                }

                foreach (var chibiEntry in chibiEntries)
                {
                    if (chibiEntry == null || string.IsNullOrEmpty(chibiEntry.AnimatorGuid))
                    {
                        continue;
                    }

                    var sourceEntry = sourceEntries.Find(e => e != null && e.ComponentIndex == chibiEntry.ComponentIndex);
                    if (sourceEntry == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(sourceEntry.AnimatorGuid) || string.Equals(sourceEntry.AnimatorGuid, chibiEntry.AnimatorGuid, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var dstEntry = dstEntries.Find(e => e != null && e.ComponentIndex == chibiEntry.ComponentIndex);
                    if (dstEntry == null)
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
                        componentIndex = chibiEntry.ComponentIndex,
                        sourceGuid = sourceEntry.AnimatorGuid,
                        targetGuid = chibiEntry.AnimatorGuid
                    });
                }
            }

            // 差分保存キーは (おちびPrefabPath, 元アバターPrefabPath)。
            // 片方でも解決できない場合は安全にスキップする。
            var sourceAvatarPrefabPath = ResolvePrefabAssetPath(sourceAvatarRoot);
            if (!string.IsNullOrEmpty(sourceChibiPrefabPath) &&
                !string.IsNullOrEmpty(sourceAvatarPrefabPath))
            {
                var payload = new MergeAnimatorDiffJson
                {
                    items = diffItems ?? new List<MergeAnimatorDiffItem>()
                };
                OCTPrefabDropdownCache.SaveMergeAnimatorDiffJson(
                    sourceChibiPrefabPath,
                    sourceAvatarPrefabPath,
                    JsonUtility.ToJson(payload, true));
            }
            else
            {
                logs.Add($"[MA MergeAnimator Diff] Save skipped: prefab path unresolved (chibi: {sourceChibiPrefabPath}, original: {sourceAvatarPrefabPath})");
            }

            logs.Add($"[MA MergeAnimator Diff] Applied: {diffItems.Count}");
        }

        /// <summary>
        /// 逆変換（おちび -> 元）時に、FaceMeshCache の差分JSONから参照を復元します。
        /// </summary>
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
                logs.Add($"[MA MergeAnimator Diff] Restore skipped: prefab path unresolved (chibi: {chibiPrefabPath}, original: {originalAvatarPrefabPath})");
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

            // 復元先は現在の avatarRoot 側。
            // パス不一致やGUID解決不可は warning ログだけ出して継続する。
            var dstMap = BuildMergeAnimatorMap(avatarRoot);
            foreach (var item in parsed.items)
            {
                if (item == null || string.IsNullOrEmpty(item.objectFullPath) || string.IsNullOrEmpty(item.sourceGuid))
                {
                    continue;
                }

                if (!dstMap.TryGetValue(item.objectFullPath, out var dstEntries) || dstEntries == null)
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: path not found: {item.objectFullPath}");
                    continue;
                }

                var dstEntry = dstEntries.Find(e => e != null && e.ComponentIndex == item.componentIndex);
                if (dstEntry == null)
                {
                    logs.Add($"[MA MergeAnimator Diff] Restore warn: component index not found: {item.objectFullPath}[{item.componentIndex}]");
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

        /// <summary>
        /// ルート配下の MergeAnimator を、ルート相対フルパスをキーに収集します。
        /// </summary>
        private static Dictionary<string, List<MergeAnimatorEntry>> BuildMergeAnimatorMap(GameObject root)
        {
            // 運用前提:
            // - 通常運用では、ユーザーが MergeAnimator 対象オブジェクトの名称/配置を大きく変更しない。
            // - そのため「複雑な推測マッチ」は採用せず、単純で追跡しやすいキーを使う。
            var map = new Dictionary<string, List<MergeAnimatorEntry>>(StringComparer.Ordinal);
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
                var componentIndex = -1;
                foreach (var component in components)
                {
                    if (!IsMergeAnimatorComponent(component))
                    {
                        continue;
                    }

                    componentIndex++;
                    if (!TryGetAnimatorAsset(component, out var animatorAsset, out var animatorGuid))
                    {
                        continue;
                    }

                    var objectPath = BuildObjectFullPath(root.transform, transform);
                    if (!map.TryGetValue(objectPath, out var entries))
                    {
                        entries = new List<MergeAnimatorEntry>();
                        map[objectPath] = entries;
                    }

                    entries.Add(new MergeAnimatorEntry
                    {
                        Component = component,
                        ComponentIndex = componentIndex,
                        AnimatorGuid = animatorGuid,
                        AnimatorAsset = animatorAsset
                    });
                }
            }

            return map;
        }

        /// <summary>
        /// 型名ベースで MergeAnimator コンポーネントか判定します（MA未参照でも動くようにするため）。
        /// </summary>
        private static bool IsMergeAnimatorComponent(Component component)
        {
            return component != null
                   && string.Equals(component.GetType().Name, "ModularAvatarMergeAnimator", StringComparison.Ordinal);
        }

        /// <summary>
        /// ルート相対パスを返します。ルート自身は "/" として扱います。
        /// </summary>
        private static string BuildObjectFullPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            var rel = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(rel) ? "/" : rel;
        }

        /// <summary>
        /// 対象 GameObject から対応する PrefabAssetPath を解決します。
        /// </summary>
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

        /// <summary>
        /// MergeAnimator の参照アセットと GUID を取得します。
        /// GUID 取得できない場合は false を返します。
        /// </summary>
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

        /// <summary>
        /// MergeAnimator の参照アセットを書き戻します。
        /// </summary>
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

        /// <summary>
        /// 参照先プロパティ（animator / animatorController）へのアクセサを解決します。
        /// 反射コストを下げるため、型ごとにキャッシュします。
        /// </summary>
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

            // フィールド優先で探索（Unity シリアライズフィールドに寄せる）。
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

            // フィールドが無い場合はプロパティを探索する。
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
