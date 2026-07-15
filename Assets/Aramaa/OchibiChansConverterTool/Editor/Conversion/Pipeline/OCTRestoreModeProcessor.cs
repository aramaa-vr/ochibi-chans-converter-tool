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
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        public static void RemoveReverseConversionAdjusters(GameObject avatarRoot, List<string> logs)
        {
            logs ??= new List<string>();
            if (avatarRoot == null)
            {
                logs.Add(L("Log.RestoreAvatarRootNullAdjuster"));
                return;
            }

            OCTModularAvatarFloorAdjusterUtility.RemoveFloorAdjustersForRestore(avatarRoot, logs);
            return;
        }

        public static void RemoveExAddMenuObjectsIfExists(GameObject avatarRoot, List<string> logs)
        {
            logs ??= new List<string>();
            if (avatarRoot == null)
            {
                logs.Add(L("Log.RestoreAvatarRootNullAddMenu"));
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
                logs.Add(L("Log.RestoreAddMenuNoTargets"));
                return;
            }

            foreach (var target in deleteTargets)
            {
                if (target == null)
                {
                    continue;
                }

                logs.Add(F("Log.RestoreAddMenuRemoved", OCTConversionLogFormatter.GetHierarchyPath(target.transform)));
                Undo.DestroyObjectImmediate(target);
            }

            logs.Add(F("Log.RestoreAddMenuRemovedCount", deleteTargets.Count));
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
