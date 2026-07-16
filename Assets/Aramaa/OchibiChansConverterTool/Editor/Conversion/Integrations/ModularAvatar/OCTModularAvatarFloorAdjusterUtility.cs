#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar Floor Adjusterの追加同期と復元時の除去をまとめます。
    /// </summary>
    internal static class OCTModularAvatarFloorAdjusterUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static void CopyModularAvatarFloorAdjustersOutsideArmature(
            GameObject srcRoot,
            GameObject dstRoot,
            Transform srcArmature,
            List<string> logs
        )
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            if (srcRoot == null || dstRoot == null)
            {
                return;
            }

            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);
            var sourceTransforms = srcRoot.GetComponentsInChildren<Transform>(includeInactive: true);

            foreach (var sourceTransform in sourceTransforms)
            {
                if (sourceTransform == null || IsUnderTransform(sourceTransform, srcArmature))
                {
                    continue;
                }

                var sourceComponent = sourceTransform.GetComponent<ModularAvatarFloorAdjuster>();
                if (sourceComponent == null)
                {
                    continue;
                }

                var relativePath = AnimationUtility.CalculateTransformPath(sourceTransform, srcRoot.transform);
                var destinationTransform = string.IsNullOrEmpty(relativePath)
                    ? dstRoot.transform
                    : dstRoot.transform.Find(relativePath);
                GameObject createdObject = null;

                if (destinationTransform == null)
                {
                    var parentPath = sourceTransform.parent == null
                        ? string.Empty
                        : AnimationUtility.CalculateTransformPath(sourceTransform.parent, srcRoot.transform);
                    var destinationParent = string.IsNullOrEmpty(parentPath)
                        ? dstRoot.transform
                        : dstRoot.transform.Find(parentPath);
                    if (destinationParent == null)
                    {
                        continue;
                    }

                    createdObject = new GameObject(sourceTransform.name);
                    Undo.RegisterCreatedObjectUndo(createdObject, L("Undo.DuplicateApply"));
                    Undo.SetTransformParent(createdObject.transform, destinationParent, L("Undo.DuplicateApply"));
                    createdObject.transform.localPosition = sourceTransform.localPosition;
                    createdObject.transform.localRotation = sourceTransform.localRotation;
                    createdObject.transform.localScale = sourceTransform.localScale;
                    createdObject.SetActive(sourceTransform.gameObject.activeSelf);
                    destinationTransform = createdObject.transform;
                }

                var destinationComponent = destinationTransform.GetComponent<ModularAvatarFloorAdjuster>();
                if (destinationComponent != null)
                {
                    continue;
                }

                try
                {
                    destinationComponent = Undo.AddComponent<ModularAvatarFloorAdjuster>(destinationTransform.gameObject);
                }
                catch
                {
                    destinationComponent = null;
                }

                if (destinationComponent == null)
                {
                    if (createdObject != null)
                    {
                        Undo.DestroyObjectImmediate(createdObject);
                    }

                    continue;
                }

                try
                {
                    EditorUtility.CopySerialized(sourceComponent, destinationComponent);
                    OCTEditorUtility.RemapObjectReferencesInObject(destinationComponent, srcRoot, dstRoot);
                }
                catch
                {
                    Undo.DestroyObjectImmediate(destinationComponent);
                    if (createdObject != null)
                    {
                        Undo.DestroyObjectImmediate(createdObject);
                    }

                    continue;
                }

                EditorUtility.SetDirty(destinationComponent);
                log.Add(
                    "Log.ComponentAdded",
                    nameof(ModularAvatarFloorAdjuster),
                    OCTConversionLogFormatter.GetHierarchyPath(destinationTransform)
                );
            }
#endif
        }

        internal static void RemoveModularAvatarFloorAdjustersForRestore(GameObject avatarRoot, List<string> logs)
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            if (avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();
            var adjusters = avatarRoot.GetComponentsInChildren<ModularAvatarFloorAdjuster>(includeInactive: true);
            foreach (var adjuster in adjusters)
            {
                if (adjuster == null)
                {
                    continue;
                }

                logs.Add(F(
                    "Log.RestoreComponentRemoved",
                    nameof(ModularAvatarFloorAdjuster),
                    OCTConversionLogFormatter.GetHierarchyPath(adjuster.transform)
                ));
                Undo.DestroyObjectImmediate(adjuster);
            }
#endif
        }

        private static bool IsUnderTransform(Transform target, Transform ancestor)
        {
            if (target == null || ancestor == null)
            {
                return false;
            }

            for (var current = target; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
