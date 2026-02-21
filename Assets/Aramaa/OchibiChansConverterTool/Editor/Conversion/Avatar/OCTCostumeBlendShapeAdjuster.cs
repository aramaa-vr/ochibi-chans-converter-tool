#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - 検出済みの衣装ルートに対し、BlendShape 同期のみを担当します。
// - ベースPrefabのBlendShape重みを参照し、衣装側の同名BlendShapeへ反映します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 同期キーは BlendShape 名（trim後）を使用します。
// - 同名キーがベースに複数ある場合、優先順で最初に見つかった値を採用します。
// - SetBlendShapeWeight に加えて SerializedObject 反映を試み、Prefab差分の安定化を狙います。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 同期キーの仕様を変える場合は既存衣装への互換性検証を必ず行うこと。
// - ベースSMR優先順（Body_base > Body > others）変更時は結果差分を確認すること。
// - 例外は握りつぶしつつ安全継続する方針のため、ログ可観測性を落とさないこと。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 検出済みの衣装ルートに対して、BlendShape の同期を適用する責務を持つクラス。
    /// </summary>
    internal static class OCTCostumeBlendShapeAdjuster
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        /// <summary>
        /// 検出済み衣装ルート群に対し、BlendShape 同期を順に実行します。
        /// </summary>
        internal static bool AdjustCostumeBlendShapes(
            GameObject basePrefabRoot,
            List<Transform> costumeRoots,
            List<string> logs
        )
        {
            if (basePrefabRoot == null)
            {
                return false;
            }

            if (costumeRoots == null || costumeRoots.Count == 0)
            {
                return true;
            }

            var baseBlendShapeWeights = BuildBaseBlendShapeWeightsByMeshAndName(basePrefabRoot, logs);

            foreach (var costumeRoot in costumeRoots)
            {
                InspectAndSyncBlendShapesUnderCostumeRoot(costumeRoot, baseBlendShapeWeights, logs);
            }

            return true;
        }

        /// <summary>
        /// ベースPrefabから BlendShape 名 -> weight の辞書を作成します。
        /// </summary>
        private static Dictionary<string, float> BuildBaseBlendShapeWeightsByMeshAndName(GameObject basePrefabRoot, List<string> logs)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (basePrefabRoot == null)
            {
                return map;
            }

            var smrs = basePrefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
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

                    var key = MakeBlendShapeKey(shapeName);
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

        /// <summary>
        /// 1つの衣装ルート配下SMRを走査し、同期可能な BlendShape を反映します。
        /// </summary>
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

                    var key = MakeBlendShapeKey(shapeName);
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

                    var key = MakeBlendShapeKey(shapeName);
                    var weight = baseBlendShapeWeights[key];

                    smr.SetBlendShapeWeight(idx, weight);
                    TrySetBlendShapeWeightSerialized(smr, idx, weight);
                }

                EditorUtility.SetDirty(smr);
                PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
                logs?.Add(F("Log.BlendshapeSynced", string.Join(", ", toApplyNames)));
            }
        }

        private static string MakeBlendShapeKey(string shapeName)
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

        /// <summary>
        /// SerializedObject 経由でも BlendShape 値を設定し、保存差分を安定化します。
        /// </summary>
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
