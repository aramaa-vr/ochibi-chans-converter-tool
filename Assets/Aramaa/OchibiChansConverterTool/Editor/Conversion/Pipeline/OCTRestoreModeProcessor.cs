#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 「復元モード（おちびちゃんズ -> 元アバター）」専用処理をまとめたクラスです。
// - 変換本体（OCTConversionPipeline）から逆変換ロジックを分離し、見通しを改善します。
//
// ============================================================================
// 重要メモ（初見向け）
// ============================================================================
// - 通常変換には触れず、復元モードでのみ呼び出されるユーティリティです。
// - 走査中にオブジェクトを削除すると取りこぼしやすいため、
//   「削除対象の収集 -> 一括削除」の順番を厳守します。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 復元モード専用のクリーンアップ処理を提供します。
    /// </summary>
    internal static class OCTRestoreModeProcessor
    {
        private static readonly Regex AddMenuDuplicateSuffixPattern = new Regex(
            "^" + OCTEditorConstants.AddMenuNameKeyword + @" \(\d+\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        /// <summary>
        /// おちびちゃんズから元アバターへ戻す際に不要な調整コンポーネントを削除します。
        /// </summary>
        public static void RemoveReverseConversionAdjusters(GameObject avatarRoot, List<string> logs)
        {
            if (avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

            var armature = OCTEditorUtility.FindAvatarMainArmature(avatarRoot.transform);
            if (armature == null)
            {
                logs.Add(F("Log.RestoreMode.ArmatureNotFound", OCTConversionLogFormatter.GetHierarchyPath(avatarRoot.transform)));
                return;
            }

            RemoveComponentByTypeName(armature.gameObject, "FloorAdjuster", logs);
            RemoveComponentByTypeName(armature.gameObject, "ModularAvatarScaleAdjuster", logs);
        }

        /// <summary>
        /// 逆変換時に、複製先アバターに残っている Ochibichans_Addmenu を削除します。
        /// </summary>
        public static void RemoveExAddMenuObjectsIfExists(GameObject avatarRoot, List<string> logs)
        {
            if (avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

            // 収集→削除の2段構成にしている理由:
            // - 走査中に Destroy すると列挙対象が変わって取りこぼしやすい
            // - 一旦候補を集めてから削除すると、処理順とログが安定する
            var removalTargets = new HashSet<GameObject>();
            var transforms = avatarRoot.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var transform in transforms)
            {
                if (transform == null || transform == avatarRoot.transform)
                {
                    continue;
                }

                var go = transform.gameObject;
                if (go == null)
                {
                    continue;
                }

                if (IsStandaloneExAddMenuObject(go))
                {
                    // 単体オブジェクトとして配置されている AddMenu を削除対象にする
                    removalTargets.Add(go);
                }

                var nearestPrefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (nearestPrefabRoot != null && nearestPrefabRoot != avatarRoot)
                {
                    var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestPrefabRoot);
                    if (IsExAddMenuPrefabPath(prefabAssetPath))
                    {
                        // ネストPrefabとして配置されている AddMenu は、インスタンスルート単位で削除する
                        removalTargets.Add(nearestPrefabRoot);
                    }
                }
            }

            if (removalTargets.Count == 0)
            {
                logs.Add(L("Log.RestoreMode.ExAddMenuNotFound"));
                return;
            }

            foreach (var target in removalTargets)
            {
                if (target == null)
                {
                    continue;
                }

                logs.Add(F("Log.RestoreMode.ExAddMenuRemovedObject", OCTConversionLogFormatter.GetHierarchyPath(target.transform)));
                Undo.DestroyObjectImmediate(target);
            }

            logs.Add(F("Log.RestoreMode.ExAddMenuRemovedCount", removalTargets.Count));
        }

        private static void RemoveComponentByTypeName(GameObject target, string typeName, List<string> logs)
        {
            if (target == null || string.IsNullOrEmpty(typeName))
            {
                return;
            }

            // TODO: 型参照ベースのより厳密な削除判定は別PRで対応予定。
            // 今回はリスク最小化のため、既存の Name 一致ロジックを維持します。
            var removedCount = 0;
            foreach (var component in target.GetComponents<Component>())
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
            }

            if (removedCount > 0)
            {
                logs.Add(F("Log.RestoreMode.ComponentRemoved", typeName, removedCount));
            }
            else
            {
                logs.Add(F("Log.RestoreMode.ComponentNotFound", typeName));
            }
        }

        private static bool IsStandaloneExAddMenuObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            // 誤削除防止のため、単純な「部分一致」は使わず、
            // AddMenu のルート名（Clone付き含む）に一致する場合のみ削除対象にする。
            var objectName = gameObject.name;
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            if (string.Equals(objectName, OCTEditorConstants.AddMenuNameKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // UnityのInstantiate名（"(Clone)"）や、Hierarchy上の重複名（" (1)" など）を許可。
            if (string.Equals(objectName, OCTEditorConstants.AddMenuNameKeyword + "(Clone)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return AddMenuDuplicateSuffixPattern.IsMatch(objectName);
        }

        private static bool IsExAddMenuPrefabPath(string prefabAssetPath)
        {
            if (string.IsNullOrEmpty(prefabAssetPath))
            {
                return false;
            }

            // パス末尾の部分一致ではなく「ファイル名の完全一致」で判定する。
            // 例: MyOchibichans_Addmenu.prefab を誤検知して削除しないようにする。
            var fileName = Path.GetFileName(prefabAssetPath);
            return string.Equals(fileName, OCTEditorConstants.AddMenuPrefabFileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
