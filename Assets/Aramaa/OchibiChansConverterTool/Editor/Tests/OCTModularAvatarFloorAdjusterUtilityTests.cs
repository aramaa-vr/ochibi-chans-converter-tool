#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Tests
{
    public sealed class OCTModularAvatarFloorAdjusterUtilityTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void CopyFloorAdjusters_CreatesMissingHierarchyUnderDestinationArmature()
        {
            var modularAvatarFloorAdjusterType = FindType(
                "nadena.dev.modular_avatar.core.ModularAvatarFloorAdjuster"
            );
            if (modularAvatarFloorAdjusterType == null)
            {
                Assert.Ignore("Modular Avatar Floor Adjuster is not installed.");
            }

            var sourceRoot = CreateObject("SourceRoot");
            var sourceArmature = CreateChild(sourceRoot, "SourceArmature");
            var sourceParent = CreateChild(sourceArmature.gameObject, "MissingParent");
            var sourceAdjuster = CreateChild(sourceParent.gameObject, "MAFloorAdjuster");
            sourceAdjuster.gameObject.AddComponent(modularAvatarFloorAdjusterType);

            var destinationRoot = CreateObject("DestinationRoot");
            var destinationArmature = CreateChild(destinationRoot, "DestinationArmature");
            var logs = new List<string>();

            OCTModularAvatarFloorAdjusterUtility.CopyFloorAdjusters(
                sourceRoot,
                destinationRoot,
                sourceArmature,
                destinationArmature,
                logs
            );

            var destinationAdjuster = destinationArmature.Find("MissingParent/MAFloorAdjuster");
            Assert.That(destinationAdjuster, Is.Not.Null);
            Assert.That(destinationAdjuster.GetComponent(modularAvatarFloorAdjusterType), Is.Not.Null);
        }

        [Test]
        public void HasAnyFloorAdjuster_DetectsSkeletalFloorAdjuster()
        {
            var skeletalType = FindType(
                "Narazaka.VRChat.FloorAdjuster.SkeletalFloorAdjuster"
            );
            if (skeletalType == null)
            {
                Assert.Ignore("Legacy Skeletal Floor Adjuster is not installed.");
            }

            var sourceRoot = CreateObject("SourceRoot");
            sourceRoot.AddComponent(skeletalType);

            Assert.That(
                OCTModularAvatarFloorAdjusterUtility.HasAnyFloorAdjuster(sourceRoot),
                Is.True
            );
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            return gameObject;
        }

        private Transform CreateChild(GameObject parent, string name)
        {
            var child = CreateObject(name);
            child.transform.SetParent(parent.transform, worldPositionStays: false);
            return child.transform;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
#endif
