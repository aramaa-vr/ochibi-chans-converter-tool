#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTCostumeScaleAdjuster.cs
//
// ============================================================================
// 概要
// ============================================================================
// - 検出済みの衣装ルートに対し、ボーン localScale 補正と BlendShape 同期を適用します。
// - 本クラスは Modular Avatar API に直接依存しない「汎用の適用処理」です。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 「どの衣装を対象にするか」は外部（Detector）から受け取ります。
// - ここは「どう適用するか」に専念する実装です。
// - Undo 記録・Prefab差分記録を行い、ユーザーが戻せることを優先します。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - マッチング順序（完全一致→Armatureパス→部分一致）は互換仕様。
// - ログキー/処理順を変更する場合は既存変換結果への影響を確認する。
// - 衣装選定ロジック（MA依存）はこのクラスに入れない。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// 検出済みの衣装ルートに対してスケール補正と BlendShape 同期を適用する責務を持つクラス。
    /// </summary>
    internal static class OCTCostumeScaleAdjuster
    {
        private const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        internal static bool AdjustCostumeScales(
            GameObject dstRoot,
            GameObject basePrefabRoot,
            List<Transform> costumeRoots,
            List<string> logs
        )
        {
            if (dstRoot == null || basePrefabRoot == null)
            {
                return false;
            }

            var log = new OCTConversionLogger(logs);
            var dstArmature = OCTEditorUtility.FindAvatarMainArmature(dstRoot.transform);
            if (dstArmature == null)
            {
                return false;
            }

            var baseArmaturePaths = BuildBaseArmatureTransformPaths(basePrefabRoot, logs);
            log.Add("Log.CostumeScaleCriteria");

            var avatarBoneScaleModifiers = BuildAvatarBoneScaleModifiers(dstArmature, baseArmaturePaths, logs);
            if (avatarBoneScaleModifiers.Count == 0)
            {
                return true;
            }

            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            logs?.Add(L("Log.CostumeScaleHeader"));
            logs?.Add(F("Log.CostumeCount", costumeRoots.Count));

            var baseBlendShapeWeights = BuildBaseBlendShapeWeightsByMeshAndName(basePrefabRoot, logs);

            foreach (var costumeRoot in costumeRoots)
            {
                AdjustOneCostume(costumeRoot, avatarBoneScaleModifiers, logs);
                InspectAndSyncBlendShapesUnderCostumeRoot(costumeRoot, baseBlendShapeWeights, logs);
            }

            return true;
        }

        private static HashSet<string> BuildBaseArmatureTransformPaths(GameObject basePrefabRoot, List<string> logs)
        {
            if (basePrefabRoot == null)
            {
                return null;
            }

            var baseArmature = OCTEditorUtility.FindAvatarMainArmature(basePrefabRoot.transform);

            if (baseArmature == null)
            {
                logs?.Add(L("Log.BaseArmatureMissing"));
                return null;
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var transforms = baseArmature.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                paths.Add(GetStableTransformPathWithSiblingIndex(t, baseArmature));
            }

            logs?.Add(F("Log.BaseArmaturePathCount", paths.Count));
            return paths;
        }

        private sealed class AvatarBoneScaleModifier
        {
            public string Name;
            public Vector3 Scale;
            public string RelativePath;
        }

        private static List<AvatarBoneScaleModifier> BuildAvatarBoneScaleModifiers(
            Transform avatarArmature,
            HashSet<string> allowedArmaturePaths,
            List<string> logs
        )
        {
            var result = new List<AvatarBoneScaleModifier>();
            if (avatarArmature == null)
            {
                return result;
            }

            int excludedCount = 0;
            var excludedPaths = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            var bones = avatarArmature.GetComponentsInChildren<Transform>(true);
            foreach (var b in bones)
            {
                if (b == null || IsNearlyOne(b.localScale))
                {
                    continue;
                }

                if (allowedArmaturePaths != null)
                {
                    var path = GetStableTransformPathWithSiblingIndex(b, avatarArmature);
                    var readablePath = GetTransformPath(b, avatarArmature);
                    if (!allowedArmaturePaths.Contains(path))
                    {
                        excludedCount++;
                        if (excludedPaths.Count < 30)
                        {
                            excludedPaths.Add(readablePath);
                        }

                        continue;
                    }
                }

                if (seenNames.Add(b.name))
                {
                    result.Add(new AvatarBoneScaleModifier
                    {
                        Name = b.name,
                        Scale = b.localScale,
                        RelativePath = AnimationUtility.CalculateTransformPath(b, avatarArmature)
                    });
                }
            }

            if (allowedArmaturePaths != null)
            {
                logs?.Add(F("Log.ArmatureExcludedCount", excludedCount));
                if (excludedPaths.Count > 0)
                {
                    logs?.Add(L("Log.ArmatureExcludedHeader"));
                    foreach (var p in excludedPaths)
                    {
                        logs?.Add(F("Log.PathEntry", p));
                    }

                    if (excludedCount > excludedPaths.Count)
                    {
                        logs?.Add(L("Log.PathEntryEllipsis"));
                    }
                }
            }

            return result;
        }

        private static void AdjustOneCostume(
            Transform costumeRoot,
            List<AvatarBoneScaleModifier> avatarBoneScaleModifiers,
            List<string> logs
        )
        {
            if (costumeRoot == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));
            if (avatarBoneScaleModifiers == null || avatarBoneScaleModifiers.Count == 0)
            {
                return;
            }

            int appliedCount = 0;
            var costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();
            var costumeArmature = OCTEditorUtility.FindAvatarMainArmature(costumeRoot);

            foreach (var modifier in avatarBoneScaleModifiers)
            {
                var temp = costumeBones;

                var matched = TryApplyScaleToFirstMatch(
                    temp,
                    bone => string.Equals(bone.name, modifier.Name, StringComparison.Ordinal),
                    modifier.Scale,
                    costumeBones,
                    logs,
                    costumeRoot,
                    modifier.Name,
                    L("Log.MatchExact"),
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
                            modifier.Name,
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
                    modifier.Name,
                    L("Log.MatchContains"),
                    ref appliedCount
                );
            }

            logs?.Add(F("Log.CostumeApplied", costumeRoot.name, appliedCount));
        }

        private static bool IsNearlyOne(Vector3 s)
        {
            return Mathf.Abs(s.x - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.y - 1f) < ScaleEpsilon &&
                   Mathf.Abs(s.z - 1f) < ScaleEpsilon;
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

            var log = new OCTConversionLogger(logs);
            log.Add(
                "Log.CostumeScaleApplied",
                costumeRoot?.name ?? L("Log.NullValue"),
                modifierKey,
                matchLabel,
                GetTransformPath(bone, costumeRoot));

            removalTarget?.Remove(bone);
            return true;
        }

        private static Dictionary<string, float> BuildBaseBlendShapeWeightsByMeshAndName(GameObject basePrefabRoot, List<string> logs)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (basePrefabRoot == null)
            {
                return map;
            }

            var smrs = basePrefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            smrs = smrs
                .OrderByDescending(GetBaseSmrPriority)
                .ThenBy(s => s != null && s.sharedMesh != null ? s.sharedMesh.name : string.Empty)
                .ToArray();

            logs?.Add(L("Log.BaseBlendShapeHeader"));
            logs?.Add(F("Log.BasePrefabRootSummary", basePrefabRoot.name, smrs.Length));

            foreach (var smr in smrs)
            {
                if (smr == null)
                {
                    continue;
                }

                var mesh = smr.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                int count = mesh.blendShapeCount;
                if (count <= 0)
                {
                    continue;
                }

                var smrPath = GetTransformPath(smr.transform, basePrefabRoot.transform);
                logs?.Add(F("Log.BaseSmrSummary", smrPath, mesh.name, count));

                for (int i = 0; i < count; i++)
                {
                    var shapeName = mesh.GetBlendShapeName(i);

                    float weight;
                    try
                    {
                        weight = smr.GetBlendShapeWeight(i);
                    }
                    catch
                    {
                        continue;
                    }

                    var key = MakeBlendShapeKey(mesh, shapeName);

                    if (!map.ContainsKey(key))
                    {
                        map.Add(key, weight);
                    }
                    else
                    {
                        logs?.Add(F("Log.BaseBlendshapeDuplicate", mesh.name, shapeName));
                    }
                }
            }

            return map;
        }

        private static void InspectAndSyncBlendShapesUnderCostumeRoot(
            Transform costumeRoot,
            Dictionary<string, float> baseBlendShapeWeights,
            List<string> logs
        )
        {
            if (costumeRoot == null)
            {
                return;
            }

            var log = new OCTConversionLogger(logs);
            var smrs = costumeRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            logs?.Add(F("Log.CostumeBlendshapeHeader", GetTransformPath(costumeRoot, costumeRoot)));
            logs?.Add(F("Log.CostumeSmrCount", smrs.Length));

            foreach (var smr in smrs)
            {
                if (smr == null)
                {
                    continue;
                }

                var mesh = smr.sharedMesh;
                var smrPath = GetTransformPath(smr.transform, costumeRoot);

                if (mesh == null)
                {
                    logs?.Add(F("Log.CostumeSmrMeshMissing", smrPath));
                    continue;
                }

                int count = mesh.blendShapeCount;
                logs?.Add(F("Log.CostumeSmrSummary", smrPath, mesh.name, count));

                if (count <= 0)
                {
                    continue;
                }

                var toApplyIndices = new List<int>();
                var toApplyNames = new List<string>();
                var allShapeNames = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    var shapeName = mesh.GetBlendShapeName(i);
                    allShapeNames.Add(shapeName);

                    if (baseBlendShapeWeights == null)
                    {
                        continue;
                    }

                    var key = MakeBlendShapeKey(mesh, shapeName);
                    if (baseBlendShapeWeights.ContainsKey(key))
                    {
                        toApplyIndices.Add(i);
                        toApplyNames.Add(shapeName);
                    }
                }

                log.AddBlendshapeEntries(allShapeNames);

                if (toApplyIndices.Count == 0)
                {
                    continue;
                }

                Undo.RecordObject(smr, L("Undo.OchibiChansConverterToolSyncBlendShapes"));

                for (int k = 0; k < toApplyIndices.Count; k++)
                {
                    int idx = toApplyIndices[k];
                    string shapeName = toApplyNames[k];

                    var key = MakeBlendShapeKey(mesh, shapeName);
                    var weight = baseBlendShapeWeights[key];

                    smr.SetBlendShapeWeight(idx, weight);
                    TrySetBlendShapeWeightSerialized(smr, idx, weight);
                }

                EditorUtility.SetDirty(smr);
                PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
                logs?.Add(F("Log.BlendshapeSynced", string.Join(", ", toApplyNames)));
            }
        }

        private static string MakeBlendShapeKey(Mesh mesh, string shapeName)
        {
            return NormalizeBlendShapeName(shapeName);
        }

        private static string NormalizeBlendShapeName(string shapeName)
        {
            return string.IsNullOrEmpty(shapeName) ? string.Empty : shapeName.Trim();
        }

        private static int GetBaseSmrPriority(SkinnedMeshRenderer smr)
        {
            if (smr == null)
            {
                return 0;
            }

            var meshName = smr.sharedMesh != null ? smr.sharedMesh.name : string.Empty;
            var goName = smr.gameObject != null ? smr.gameObject.name : string.Empty;

            if (string.Equals(meshName, "Body_base", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(goName, "Body_base", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (string.Equals(meshName, "Body", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(goName, "Body", StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }

            return 0;
        }

        private static string GetStableTransformPathWithSiblingIndex(Transform target, Transform root)
        {
            if (target == null)
            {
                return L("Log.NullValue");
            }

            if (root == null)
            {
                return target.name;
            }

            var segments = new List<string>();
            var current = target;

            while (current != null)
            {
                if (current == root)
                {
                    segments.Add(root.name);
                    break;
                }

                var parent = current.parent;
                if (parent == null)
                {
                    segments.Add(current.name);
                    break;
                }

                int ordinal = 0;
                int sameNameCount = 0;

                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == null || !string.Equals(child.name, current.name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (child == current)
                    {
                        ordinal = sameNameCount;
                    }

                    sameNameCount++;
                }

                segments.Add(sameNameCount > 1 ? $"{current.name}#{ordinal}" : current.name);
                current = parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
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
            if (string.IsNullOrEmpty(rel))
            {
                return root.name;
            }

            return root.name + "/" + rel;
        }

        private static void TrySetBlendShapeWeightSerialized(SkinnedMeshRenderer smr, int blendShapeIndex, float weight)
        {
            try
            {
                var so = new SerializedObject(smr);
                var prop = so.FindProperty("m_BlendShapeWeights");
                if (prop == null || !prop.isArray)
                {
                    return;
                }

                if (blendShapeIndex < 0 || blendShapeIndex >= prop.arraySize)
                {
                    return;
                }

                prop.GetArrayElementAtIndex(blendShapeIndex).floatValue = weight;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            catch
            {
                // SetBlendShapeWeight が適用済みのため安全に無視
            }
        }
    }
}
#endif
