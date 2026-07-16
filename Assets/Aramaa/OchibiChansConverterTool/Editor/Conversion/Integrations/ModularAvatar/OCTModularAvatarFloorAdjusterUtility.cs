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
                OCTEditorConstants.LegacySkeletalFloorAdjusterTypeName,
                logs,
                "Log.FloorAdjusterConflictRemoved",
                notFoundLogKey: null
            );
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
            RemoveComponentsByTypeName(
                avatarRoot,
                OCTEditorConstants.LegacySkeletalFloorAdjusterTypeName,
                logs,
                "Log.RestoreComponentRemoved",
                "Log.RestoreComponentNotFound"
            );
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
        /// Floor Adjusterを変換先へ複製します。
        /// 変換先に対応するTransformが無い場合は、必要な階層ごと作成します。
        /// </summary>
        internal static void CopyFloorAdjusters(
            GameObject srcRoot,
            GameObject dstRoot,
            Transform srcArmature,
            Transform dstArmature,
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
                if (srcTransform == null)
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
                        srcArmature,
                        dstArmature,
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
                        srcArmature,
                        dstArmature,
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
            Transform srcArmature,
            Transform dstArmature,
            OCTConversionLogger log,
            ref int copiedCount,
            ref int skippedCount
        )
        {
            var createdObjects = new List<GameObject>();
            var dstTransform = FindOrCreateDestinationTransform(
                srcTransform,
                srcRoot.transform,
                dstRoot.transform,
                srcArmature,
                dstArmature,
                createdObjects
            );
            if (dstTransform == null)
            {
                skippedCount++;
                return;
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
                DestroyCreatedObjects(createdObjects);
                skippedCount++;
                return;
            }

            try
            {
                EditorUtility.CopySerialized(sourceComponent, destinationComponent);
            }
            catch
            {
                if (addedComponent)
                {
                    Undo.DestroyObjectImmediate(destinationComponent);
                }
                DestroyCreatedObjects(createdObjects);
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

        private static Transform FindOrCreateDestinationTransform(
            Transform srcTransform,
            Transform srcRoot,
            Transform dstRoot,
            Transform srcArmature,
            Transform dstArmature,
            List<GameObject> createdObjects
        )
        {
            if (srcTransform == null || srcRoot == null || dstRoot == null)
            {
                return null;
            }

            var sourceBase = srcRoot;
            var destinationBase = dstRoot;
            if (srcArmature != null && dstArmature != null && IsUnderTransform(srcTransform, srcArmature))
            {
                sourceBase = srcArmature;
                destinationBase = dstArmature;
            }

            var sourceChain = new List<Transform>();
            for (var current = srcTransform; current != null && current != sourceBase; current = current.parent)
            {
                sourceChain.Add(current);
            }

            if (srcTransform != sourceBase && (sourceChain.Count == 0 || sourceChain[sourceChain.Count - 1].parent != sourceBase))
            {
                return null;
            }

            sourceChain.Reverse();
            var destination = destinationBase;
            foreach (var sourceChild in sourceChain)
            {
                var child = destination.Find(sourceChild.name);
                if (child == null)
                {
                    var createdObject = new GameObject(sourceChild.name);
                    Undo.RegisterCreatedObjectUndo(createdObject, L("Undo.SyncFloorAdjuster"));
                    Undo.SetTransformParent(createdObject.transform, destination, L("Undo.SyncFloorAdjuster"));
                    Undo.RecordObject(createdObject.transform, L("Undo.SyncFloorAdjuster"));
                    createdObject.transform.localPosition = sourceChild.localPosition;
                    createdObject.transform.localRotation = sourceChild.localRotation;
                    createdObject.transform.localScale = sourceChild.localScale;
                    createdObject.SetActive(sourceChild.gameObject.activeSelf);
                    createdObjects.Add(createdObject);
                    child = createdObject.transform;
                }

                destination = child;
            }

            return destination;
        }

        private static void DestroyCreatedObjects(List<GameObject> createdObjects)
        {
            if (createdObjects == null)
            {
                return;
            }

            for (var i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    Undo.DestroyObjectImmediate(createdObjects[i]);
                }
            }
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
            return string.Equals(typeName, OCTEditorConstants.LegacyFloorAdjusterTypeName, StringComparison.Ordinal)
                   || string.Equals(typeName, OCTEditorConstants.LegacySkeletalFloorAdjusterTypeName, StringComparison.Ordinal);
        }

    }
}
#endif
