#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar の型・メンバーへ「コンパイル時依存なし」でアクセスする
//   リフレクションヘルパです。
// - MA 未導入・API 変更時でもツール全体を落とさず、失敗を one-shot warning として通知します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - ここでの失敗は例外で止めず「null / false で返す」設計です。
// - 反射アクセスで失敗したかどうかは内部フラグで保持し、呼び出し元 UI で
//   「変換が不完全な可能性」のダイアログ表示に利用します。
// - 文字列型名は MA 側の変更影響を受けるため、更新時はまずこのファイルを確認してください。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MA への新しい反射アクセスは可能な限りこのクラスへ追加し、他クラスへ分散させない。
// - warning は必ず WarnOnce 経由で出し、コンソールスパムを避ける。
// - 失敗時に throw しない（変換継続を最優先）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar 反射アクセスを集約するユーティリティ。
    /// </summary>
    internal static class OCTModularAvatarReflection
    {
        // MA 側公開型のフルネーム（assembly-qualified は使わない）
        internal const string BoneProxyTypeName = "nadena.dev.modular_avatar.core.ModularAvatarBoneProxy";
        internal const string MergeArmatureTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature";
        internal const string MeshSettingsTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMeshSettings";

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);
        private static bool _hadReflectionAccessFailure;
        private static double _lastTypeCacheClearTime;
        private const double TypeCacheClearIntervalSeconds = 60d;

        static OCTModularAvatarReflection()
        {
            _lastTypeCacheClearTime = EditorApplication.timeSinceStartup;
            AssemblyReloadEvents.beforeAssemblyReload += ClearTypeCache;
            EditorApplication.playModeStateChanged += _ => MaybeClearTypeCachePeriodically();
        }

        internal static bool TryGetBoneProxyType(out Type type) => TryGetType(BoneProxyTypeName, out type);
        internal static bool TryGetMergeArmatureType(out Type type) => TryGetType(MergeArmatureTypeName, out type);
        internal static bool TryGetMeshSettingsType(out Type type) => TryGetType(MeshSettingsTypeName, out type);

        /// <summary>
        /// フルネームから型を取得します。結果はキャッシュされます。
        /// </summary>
        internal static bool TryGetType(string fullName, out Type type)
        {
            MaybeClearTypeCachePeriodically();

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

        /// <summary>
        /// ロード済みアセンブリを走査して型を探します。
        /// </summary>
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

        /// <summary>
        /// BoneProxy.target を反射で取得します。失敗時は null。
        /// </summary>
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

        /// <summary>
        /// BoneProxy.attachmentMode の enum 名（ToString）を反射で取得します。
        /// </summary>
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

        /// <summary>
        /// MergeArmature.GetBonesMapping を反射で呼び出します。
        /// </summary>
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

        /// <summary>
        /// ValueTuple 相当オブジェクトから Item1 / Item2 などの Transform を取り出します。
        /// </summary>
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

        /// <summary>
        /// 任意型のコンポーネントを子孫から列挙します。失敗時は空列挙を返します。
        /// </summary>
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

        /// <summary>
        /// 1 変換実行分の失敗フラグを初期化します。
        /// </summary>
        internal static void ResetReflectionFailureFlag()
        {
            _hadReflectionAccessFailure = false;
        }

        /// <summary>
        /// 反射失敗フラグを取得し、同時にクリアします。
        /// </summary>
        internal static bool ConsumeReflectionFailureFlag()
        {
            var hadFailure = _hadReflectionAccessFailure;
            _hadReflectionAccessFailure = false;
            return hadFailure;
        }

        private static void MaybeClearTypeCachePeriodically()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastTypeCacheClearTime < TypeCacheClearIntervalSeconds)
            {
                return;
            }

            ClearTypeCache();
        }

        private static void ClearTypeCache()
        {
            TypeCache.Clear();
            _lastTypeCacheClearTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// one-shot warning を出力します。ここを経由した時点で「反射失敗あり」とみなします。
        /// </summary>
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
