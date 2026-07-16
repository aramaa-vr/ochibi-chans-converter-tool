#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar Floor Adjuster と旧Floor Adjusterの互換処理をまとめます。
    /// </summary>
    internal static class OCTModularAvatarFloorAdjusterUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static bool HasModularAvatarFloorAdjuster(GameObject avatarRoot)
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            return avatarRoot != null
                   && avatarRoot.GetComponentsInChildren<ModularAvatarFloorAdjuster>(includeInactive: true).Length > 0;
#else
            return false;
#endif
        }

        internal static bool HasAnyFloorAdjuster(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return false;
            }

            if (HasModularAvatarFloorAdjuster(avatarRoot))
            {
                return true;
            }

            return avatarRoot
                .GetComponentsInChildren<Component>(includeInactive: true)
                .Any(IsLegacyFloorAdjusterComponent);
        }

        internal static bool ShouldCopyFloorAdjusterComponent(GameObject sourceRoot, Component sourceComponent)
        {
            if (sourceComponent == null || !IsFloorAdjusterComponent(sourceComponent))
            {
                return true;
            }

#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            if (sourceComponent is ModularAvatarFloorAdjuster)
            {
                return true;
            }

            if (HasModularAvatarFloorAdjuster(sourceRoot))
            {
                return false;
            }
#endif

            return true;
        }

        /// <summary>
        /// 変換元のFloor Adjusterを正として、変換先に残っている旧/新方式の競合を除去します。
        /// </summary>
        internal static void RemoveConflictingFloorAdjusters(GameObject avatarRoot, List<string> logs)
        {
            if (avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            RemoveModularAvatarFloorAdjusters(
                avatarRoot,
                logs,
                "Log.FloorAdjusterConflictRemoved",
                notFoundLogKey: null,
                removeEmptyGameObject: false
            );
#endif
            RemoveComponentsByTypeName(
                avatarRoot,
                OCTEditorConstants.LegacyFloorAdjusterTypeName,
                logs,
                "Log.FloorAdjusterConflictRemoved",
                notFoundLogKey: null
            );
        }

        /// <summary>
        /// 逆変換時に旧/新方式のFloor Adjusterを除去します。
        /// </summary>
        internal static void RemoveFloorAdjustersForRestore(GameObject avatarRoot, List<string> logs)
        {
            logs ??= new List<string>();
            if (avatarRoot == null)
            {
                logs.Add(L("Log.RestoreAvatarRootNullAdjuster"));
                return;
            }

#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            RemoveModularAvatarFloorAdjusters(
                avatarRoot,
                logs,
                "Log.RestoreComponentRemoved",
                "Log.RestoreComponentNotFound",
                removeEmptyGameObject: true
            );
#endif
            var armature = OCTEditorUtility.FindAvatarMainArmature(avatarRoot.transform);
            if (armature == null)
            {
                logs.Add(F(
                    "Log.RestoreArmatureMissing",
                    OCTConversionLogFormatter.GetHierarchyPath(avatarRoot.transform)
                ));
                return;
            }

            logs.Add(F(
                "Log.RestoreArmatureAdjusterStart",
                OCTConversionLogFormatter.GetHierarchyPath(armature)
            ));
            RemoveComponentsByTypeName(
                armature.gameObject,
                OCTEditorConstants.LegacyFloorAdjusterTypeName,
                logs,
                "Log.RestoreComponentRemoved",
                "Log.RestoreComponentNotFound"
            );
        }

        /// <summary>
        /// Armature外に配置されるFloor Adjusterを変換先へ複製します。
        /// Armature上の旧方式は、既存のArmatureコンポーネント同期で処理します。
        /// </summary>
        internal static void CopyFloorAdjustersOutsideArmature(
            GameObject srcRoot,
            GameObject dstRoot,
            Transform srcArmature,
            List<string> logs
        )
        {
            if (srcRoot == null || dstRoot == null)
            {
                return;
            }

            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);
            var sourceTransforms = srcRoot.GetComponentsInChildren<Transform>(includeInactive: true);
            var keepModularAvatarFloorAdjuster = HasModularAvatarFloorAdjuster(srcRoot);
            var copiedCount = 0;
            var skippedCount = 0;

            foreach (var srcTransform in sourceTransforms)
            {
                if (srcTransform == null || IsUnderTransform(srcTransform, srcArmature))
                {
                    continue;
                }

#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
                var modularAvatarFloorAdjuster = srcTransform.GetComponent<ModularAvatarFloorAdjuster>();
                if (modularAvatarFloorAdjuster != null)
                {
                    CopyFloorAdjusterComponent(
                        modularAvatarFloorAdjuster,
                        srcTransform,
                        srcRoot,
                        dstRoot,
                        log,
                        ref copiedCount,
                        ref skippedCount
                    );
                }
#endif

                if (keepModularAvatarFloorAdjuster)
                {
                    continue;
                }

                var legacySourceComponents = srcTransform
                    .GetComponents<Component>()
                    .Where(IsLegacyFloorAdjusterComponent);

                foreach (var sourceComponent in legacySourceComponents)
                {
                    CopyFloorAdjusterComponent(
                        sourceComponent,
                        srcTransform,
                        srcRoot,
                        dstRoot,
                        log,
                        ref copiedCount,
                        ref skippedCount
                    );
                }
            }

            if (copiedCount > 0)
            {
                logs.Add(F("Log.MissingComponentsAdded", copiedCount));
            }

            if (skippedCount > 0)
            {
                logs.Add(F("Log.AddMissingComponentsPathMissing", skippedCount));
            }
        }

        internal static bool IsFloorAdjusterComponent(Component component)
        {
            if (component == null)
            {
                return false;
            }

#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            if (component is ModularAvatarFloorAdjuster)
            {
                return true;
            }
#endif

            return IsLegacyFloorAdjusterComponent(component);
        }

        private static void CopyFloorAdjusterComponent(
            Component sourceComponent,
            Transform srcTransform,
            GameObject srcRoot,
            GameObject dstRoot,
            OCTConversionLogger log,
            ref int copiedCount,
            ref int skippedCount
        )
        {
            var relativePath = BuildRelativePathFromRoot(srcRoot.transform, srcTransform);
            var dstTransform = string.IsNullOrEmpty(relativePath)
                ? dstRoot.transform
                : dstRoot.transform.Find(relativePath);
            GameObject createdObject = null;

            if (dstTransform == null)
            {
                var parentPath = BuildRelativePathFromRoot(srcRoot.transform, srcTransform.parent);
                var dstParent = string.IsNullOrEmpty(parentPath)
                    ? dstRoot.transform
                    : dstRoot.transform.Find(parentPath);
                if (dstParent == null)
                {
                    skippedCount++;
                    return;
                }

                createdObject = new GameObject(srcTransform.name);
                Undo.RegisterCreatedObjectUndo(createdObject, L("Undo.SyncFloorAdjuster"));
                Undo.SetTransformParent(createdObject.transform, dstParent, L("Undo.SyncFloorAdjuster"));
                dstTransform = createdObject.transform;
            }

            Undo.RecordObject(dstTransform, L("Undo.SyncFloorAdjuster"));
            Undo.RecordObject(dstTransform.gameObject, L("Undo.SyncFloorAdjuster"));
            dstTransform.localPosition = srcTransform.localPosition;
            dstTransform.localRotation = srcTransform.localRotation;
            dstTransform.localScale = srcTransform.localScale;
            dstTransform.gameObject.SetActive(srcTransform.gameObject.activeSelf);

            var destinationComponent = dstTransform.GetComponent(sourceComponent.GetType());
            var addedComponent = false;
            if (destinationComponent == null)
            {
                try
                {
                    destinationComponent = Undo.AddComponent(dstTransform.gameObject, sourceComponent.GetType());
                    addedComponent = destinationComponent != null;
                }
                catch
                {
                    destinationComponent = null;
                }
            }

            if (destinationComponent == null)
            {
                if (createdObject != null)
                {
                    Undo.DestroyObjectImmediate(createdObject);
                }
                skippedCount++;
                return;
            }

            try
            {
                EditorUtility.CopySerialized(sourceComponent, destinationComponent);
            }
            catch
            {
                if (createdObject != null)
                {
                    Undo.DestroyObjectImmediate(createdObject);
                }
                else if (addedComponent)
                {
                    Undo.DestroyObjectImmediate(destinationComponent);
                }
                skippedCount++;
                return;
            }

            OCTEditorUtility.RemapObjectReferencesInObject(destinationComponent, srcRoot, dstRoot);
            EditorUtility.SetDirty(destinationComponent);
            copiedCount++;
            log.Add(
                "Log.ComponentAdded",
                sourceComponent.GetType().Name,
                OCTConversionLogFormatter.GetHierarchyPath(dstTransform)
            );
        }

        private static void RemoveModularAvatarFloorAdjusters(
            GameObject avatarRoot,
            List<string> logs,
            string removedLogKey,
            string notFoundLogKey,
            bool removeEmptyGameObject
        )
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            var adjusters = avatarRoot.GetComponentsInChildren<ModularAvatarFloorAdjuster>(includeInactive: true);
            foreach (var adjuster in adjusters)
            {
                if (adjuster == null)
                {
                    continue;
                }

                logs.Add(F(
                    removedLogKey,
                    nameof(ModularAvatarFloorAdjuster),
                    OCTConversionLogFormatter.GetHierarchyPath(adjuster.transform)
                ));

                if (removeEmptyGameObject && CanRemoveEmptyFloorAdjusterObject(adjuster.gameObject, avatarRoot))
                {
                    Undo.DestroyObjectImmediate(adjuster.gameObject);
                }
                else
                {
                    Undo.DestroyObjectImmediate(adjuster);
                }
            }

            if (adjusters.Length == 0 && !string.IsNullOrEmpty(notFoundLogKey))
            {
                logs.Add(F(notFoundLogKey, nameof(ModularAvatarFloorAdjuster)));
            }
#endif
        }

        private static bool CanRemoveEmptyFloorAdjusterObject(GameObject target, GameObject avatarRoot)
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            if (target == null || target == avatarRoot || target.transform.childCount > 0)
            {
                return false;
            }

            return target
                .GetComponents<Component>()
                .All(component => component is Transform || component is ModularAvatarFloorAdjuster);
#else
            return false;
#endif
        }

        private static void RemoveComponentsByTypeName(
            GameObject target,
            string typeName,
            List<string> logs,
            string removedLogKey,
            string notFoundLogKey
        )
        {
            if (target == null || string.IsNullOrEmpty(typeName))
            {
                return;
            }

            var removedCount = 0;
            var transforms = target.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (var transform in transforms)
            {
                if (transform == null)
                {
                    continue;
                }

                var components = transform.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null || !string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Undo.DestroyObjectImmediate(component);
                    removedCount++;
                    if (!string.IsNullOrEmpty(removedLogKey))
                    {
                        logs.Add(F(
                            removedLogKey,
                            typeName,
                            OCTConversionLogFormatter.GetHierarchyPath(transform)
                        ));
                    }
                }
            }

            if (removedCount == 0 && !string.IsNullOrEmpty(notFoundLogKey))
            {
                logs.Add(F(notFoundLogKey, typeName));
            }
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

        private static bool IsLegacyFloorAdjusterComponent(Component component)
        {
            if (component == null)
            {
                return false;
            }

            var typeName = component.GetType().Name;
            return string.Equals(typeName, OCTEditorConstants.LegacyFloorAdjusterTypeName, StringComparison.Ordinal);
        }

        private static string BuildRelativePathFromRoot(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (target == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return string.Empty;
            }

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
#endif
