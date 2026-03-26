#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 逆変換（おちびちゃんズ -> 元アバター）専用の処理をまとめたプロセッサーです。
    /// </summary>
    internal static class OCTRestoreModeProcessor
    {
        public static void RemoveReverseConversionAdjusters(GameObject avatarRoot, List<string> logs)
        {
            logs ??= new List<string>();
            if (avatarRoot == null)
            {
                logs.Add("[Restore] avatarRoot が null のため補正コンポーネント削除をスキップしました。");
                return;
            }

            var armature = OCTEditorUtility.FindAvatarMainArmature(avatarRoot.transform);
            if (armature == null)
            {
                logs.Add($"[Restore] Armature が見つからないため補正コンポーネント削除をスキップ: {OCTConversionLogFormatter.GetHierarchyPath(avatarRoot.transform)}");
                return;
            }

            logs.Add($"[Restore] Armature 補正コンポーネント削除開始: {OCTConversionLogFormatter.GetHierarchyPath(armature)}");
            RemoveComponentByTypeName(armature.gameObject, "FloorAdjuster", logs);
            RemoveComponentByTypeName(armature.gameObject, "ModularAvatarScaleAdjuster", logs);
        }

        public static void RemoveExAddMenuObjectsIfExists(GameObject avatarRoot, List<string> logs)
        {
            logs ??= new List<string>();
            if (avatarRoot == null)
            {
                logs.Add("[Restore] avatarRoot が null のため AddMenu 削除をスキップしました。");
                return;
            }

            var deleteTargets = new HashSet<GameObject>();
            var transforms = avatarRoot.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                var go = t.gameObject;
                if (go == null || go == avatarRoot)
                {
                    continue;
                }

                if (IsStandaloneExAddMenuObject(go))
                {
                    deleteTargets.Add(go);
                }

                var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (instanceRoot == null || instanceRoot != go)
                {
                    continue;
                }

                var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                if (IsExAddMenuPrefabPath(prefabPath))
                {
                    deleteTargets.Add(instanceRoot);
                }
            }

            if (deleteTargets.Count == 0)
            {
                logs.Add("[Restore] AddMenu 削除対象なし。");
                return;
            }

            foreach (var target in deleteTargets)
            {
                if (target == null)
                {
                    continue;
                }

                logs.Add($"[Restore] AddMenu 削除: {OCTConversionLogFormatter.GetHierarchyPath(target.transform)}");
                Undo.DestroyObjectImmediate(target);
            }

            logs.Add($"[Restore] AddMenu 削除件数: {deleteTargets.Count}");
        }

        internal static bool IsStandaloneExAddMenuObject(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            var normalizedName = NormalizeStandaloneAddMenuName(go.name);
            return string.Equals(normalizedName, OCTEditorConstants.AddMenuNameKeyword, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsExAddMenuPrefabPath(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(prefabPath),
                OCTEditorConstants.AddMenuPrefabFileName,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static void RemoveComponentByTypeName(GameObject target, string typeName, List<string> logs)
        {
            logs ??= new List<string>();
            if (target == null || string.IsNullOrEmpty(typeName))
            {
                return;
            }

            var removedCount = 0;
            var transforms = target.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                var components = t.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    if (!string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Undo.DestroyObjectImmediate(component);
                    removedCount++;
                    logs.Add($"[Restore] コンポーネント削除: {typeName} @ {OCTConversionLogFormatter.GetHierarchyPath(t)}");
                }
            }

            if (removedCount == 0)
            {
                logs.Add($"[Restore] コンポーネント未検出: {typeName}");
            }
        }

        private static string NormalizeStandaloneAddMenuName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return string.Empty;
            }

            var normalized = objectName.Trim();
            normalized = Regex.Replace(normalized, @"\(Clone\)$", string.Empty, RegexOptions.IgnoreCase).Trim();
            normalized = Regex.Replace(normalized, @"\(\d+\)$", string.Empty).Trim();
            return normalized;
        }
    }
}
#endif
