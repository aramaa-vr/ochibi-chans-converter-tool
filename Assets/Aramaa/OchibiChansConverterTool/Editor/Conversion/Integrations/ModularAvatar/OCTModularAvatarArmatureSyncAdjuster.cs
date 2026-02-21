#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// ModularAvatarMergeArmature のボーン対応表を利用し、衣装側ボーンの localScale を補正する。
    /// </summary>
    internal static class OCTModularAvatarArmatureSyncAdjuster
    {
        private const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static bool AdjustByMergeArmatureMapping(List<Transform> costumeRoots, List<string> logs)
        {
            new OCTConversionLogger(logs).Add("Log.CostumeScaleCriteria");

            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            logs?.Add(L("Log.CostumeScaleHeader"));
            logs?.Add(F("Log.CostumeCount", costumeRoots.Count));

            foreach (var costumeRoot in costumeRoots)
            {
                AdjustOneCostume(costumeRoot, logs);
            }

            return true;
        }

        private sealed class AvatarBoneScaleModifier
        {
            public string Name;
            public Vector3 Scale;
            public string RelativePath;
            public Transform PreferredBone;
        }

#if CHIBI_MODULAR_AVATAR
        private static void AdjustOneCostume(Transform costumeRoot, List<string> logs)
        {
            if (costumeRoot == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));

            var avatarBoneScaleModifiers = BuildAvatarBoneScaleModifiers(costumeRoot, logs);
            if (avatarBoneScaleModifiers.Count == 0)
            {
                logs?.Add(F("Log.CostumeApplied", costumeRoot.name, 0));
                return;
            }

            var appliedCount = 0;
            var costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();
            var costumeArmature = OCTEditorUtility.FindAvatarMainArmature(costumeRoot);

            ApplyScaleModifiers(
                avatarBoneScaleModifiers,
                costumeBones,
                costumeArmature,
                logs,
                costumeRoot,
                ref appliedCount
            );

            logs?.Add(F("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }

        private static List<AvatarBoneScaleModifier> BuildAvatarBoneScaleModifiers(Transform costumeRoot, List<string> logs)
        {
            var result = new List<AvatarBoneScaleModifier>();

            var mergeArmature = costumeRoot.GetComponentInParent<ModularAvatarMergeArmature>(true);
            if (mergeArmature == null)
            {
                logs?.Add($"[CostumeScale] skip: merge armature not found for '{costumeRoot.name}'");
                return result;
            }

            var mapping = mergeArmature.GetBonesMapping();
            if (mapping == null || mapping.Count == 0)
            {
                logs?.Add($"[CostumeScale] skip: bone mapping empty for '{costumeRoot.name}'");
                return result;
            }

            var baseRoot = mergeArmature.mergeTargetObject != null ? mergeArmature.mergeTargetObject.transform : null;
            foreach (var pair in mapping)
            {
                var baseBone = pair.Item1;
                var outfitBone = pair.Item2;
                if (baseBone == null || outfitBone == null || !outfitBone.IsChildOf(costumeRoot))
                {
                    continue;
                }

                if (IsNearlyOne(baseBone.localScale))
                {
                    continue;
                }

                result.Add(new AvatarBoneScaleModifier
                {
                    Name = baseBone.name,
                    Scale = baseBone.localScale,
                    RelativePath = AnimationUtility.CalculateTransformPath(baseBone, baseRoot),
                    PreferredBone = outfitBone
                });
            }

            return result;
        }
#else
        private static void AdjustOneCostume(Transform costumeRoot, List<string> logs)
        {
            _ = costumeRoot;
            _ = logs;
        }
#endif

        private static bool IsNearlyOne(Vector3 s)
        {
            return Mathf.Abs(s.x - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.y - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.z - 1f) < ScaleEpsilon;
        }

        private static void ApplyScaleModifiers(
            List<AvatarBoneScaleModifier> avatarBoneScaleModifiers,
            List<Transform> costumeBones,
            Transform costumeArmature,
            List<string> logs,
            Transform costumeRoot,
            ref int appliedCount
        )
        {
            foreach (var modifier in avatarBoneScaleModifiers)
            {
                var temp = costumeBones;
                var modifierKeyForLog = BuildModifierKeyForLog(modifier);

                var matched = TryApplyScaleToFirstMatch(
                    temp,
                    bone => string.Equals(bone.name, modifier.Name, StringComparison.Ordinal),
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifierKeyForLog,
                    L("Log.MatchExact"),
                    ref appliedCount
                );

                if (matched)
                {
                    continue;
                }

                matched = TryApplyScaleToBone(
                    modifier.PreferredBone,
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifierKeyForLog,
                    L("Log.MatchArmature"),
                    ref appliedCount
                );

                if (matched)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(modifier.RelativePath) && costumeArmature != null)
                {
                    var normalizedPath = OCTEditorUtility.NormalizeRelPathFor(costumeArmature, modifier.RelativePath);
                    var candidate = string.IsNullOrEmpty(normalizedPath)
                        ? costumeArmature
                        : costumeArmature.Find(normalizedPath);
                    if (candidate != null && !costumeBones.Contains(candidate))
                    {
                        candidate = null;
                    }

                    if (TryApplyScaleToBone(
                            candidate,
                            modifier.Scale,
                            costumeBones,
                            logs,
                            costumeRoot,
                            modifierKeyForLog,
                            L("Log.MatchArmature"),
                            ref appliedCount
                        ))
                    {
                        continue;
                    }
                }

                TryApplyScaleToFirstMatch(
                    temp,
                    bone => bone.name.Contains(modifier.Name),
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifierKeyForLog,
                    L("Log.MatchContains"),
                    ref appliedCount
                );
            }
        }

        private static string BuildModifierKeyForLog(AvatarBoneScaleModifier modifier)
        {
            if (modifier == null)
            {
                return L("Log.NullValue");
            }

            if (string.IsNullOrEmpty(modifier.RelativePath))
            {
                return modifier.Name;
            }

            return $"{modifier.Name} ({modifier.RelativePath})";
        }

        private static bool TryApplyScaleToFirstMatch(
            IEnumerable<Transform> bones,
            Func<Transform, bool> predicate,
            Vector3 scaleModifier,
            List<Transform> removalTarget,
            List<string> logs,
            Transform costumeRoot,
            string modifierKey,
            string matchLabel,
            ref int appliedCount
        )
        {
            foreach (var bone in bones)
            {
                if (bone == null || !predicate(bone))
                {
                    continue;
                }

                return TryApplyScaleToBone(
                    bone,
                    scaleModifier,
                    removalTarget,
                    logs,
                    costumeRoot,
                    modifierKey,
                    matchLabel,
                    ref appliedCount
                );
            }

            return false;
        }

        private static bool TryApplyScaleToBone(
            Transform bone,
            Vector3 scaleModifier,
            List<Transform> removalTarget,
            List<string> logs,
            Transform costumeRoot,
            string modifierKey,
            string matchLabel,
            ref int appliedCount
        )
        {
            if (bone == null)
            {
                return false;
            }

            bone.localScale = Vector3.Scale(bone.localScale, scaleModifier);
            EditorUtility.SetDirty(bone);
            appliedCount++;

            new OCTConversionLogger(logs).Add(
                "Log.CostumeScaleApplied",
                costumeRoot?.name ?? L("Log.NullValue"),
                modifierKey,
                matchLabel,
                FormatMatchedBoneForLog(bone, costumeRoot));

            removalTarget?.Remove(bone);
            return true;
        }

        private static string FormatMatchedBoneForLog(Transform bone, Transform costumeRoot)
        {
            if (bone == null)
            {
                return L("Log.NullValue");
            }

            var path = GetTransformPath(bone, costumeRoot);
            return $"{bone.name} ({path})";
        }

        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == null)
            {
                return L("Log.NullValue");
            }

            if (root == null)
            {
                return target.name;
            }

            var rel = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(rel) ? root.name : root.name + "/" + rel;
        }
    }
}
#endif
