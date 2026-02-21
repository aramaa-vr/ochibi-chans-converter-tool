#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTSkinnedMeshUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - SkinnedMeshRenderer（SMR）の “BlendShape ウェイトだけ” を、複製先へ安全に同期します
// - SMR は触れる項目が多いので、事故防止のため処理をここに閉じ込めています
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - このユーティリティは “BlendShapes 以外” を絶対に上書きしません（重要）
// - SMR が多いアバターでも重くなりにくいように、dstRoot の探索結果を辞書化して使います
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 変更前に「どのアセット/どの階層を触るか」をコメントに残す（事故防止）
// - Editor 拡張は必ず Undo を記録する（ユーザーが戻せることが最優先）
// - Prefab アセットを勝手に更新しない（Scene 上の対象だけを変更）
// - 処理順が仕様なので、並べ替える時は README とコメントも更新する
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// SkinnedMeshRenderer の BlendShape ウェイトのみを、変換元 → 複製先へ安全に同期するユーティリティです。
    /// </summary>
    internal static class OCTSkinnedMeshUtility
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        /// <summary>
        /// 変換元（srcRoot）内の SkinnedMeshRenderer を走査し、
        /// 複製先（dstRoot）内の “同パス / 同名” の SkinnedMeshRenderer に対して
        /// BlendShape（ウェイト）だけを同期します。
        /// </summary>
        public static void CopySkinnedMeshRenderersBlendShapesOnly(GameObject srcRoot, GameObject dstRoot)
        {
            if (srcRoot == null || dstRoot == null)
            {
                return;
            }

            var srcSmrs = srcRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (srcSmrs == null || srcSmrs.Length == 0)
            {
                return;
            }

            // ----------------------------------------------------------------
            // 最適化ポイント（初心者向け解説）：
            // - Transform.Find は呼ぶたびに階層を探しに行くので、回数が増えると重くなりがちです
            // - そこで “最初に 1 回だけ” dstRoot 配下の Transform 一覧を作り、
            //   以降は Dictionary（辞書）で高速に取り出します
            // - これにより、SMR が多いアバターでも処理時間が増えにくくなります
            // ----------------------------------------------------------------
            // 元の仕様（優先順位）は維持：
            // 1) 同パス（階層が同じ）で一致するものに適用
            // 2) 見つからなければ同名を全探索して適用
            // ----------------------------------------------------------------

            // - これまで「srcSmr ごとに dstRoot.Find(relPath)」「srcSmr ごとに名前探索」を行っていたため、
            //   SMR 数が多いと Find のコストが増えやすい
            // - ここでは “dstRoot 配下の Transform を一度だけ走査” し、
            //   1) 相対パス → Transform
            //   2) 名前 → Transform一覧
            //   の2種類の辞書を作って高速に引けるようにします
            // ----------------------------------------------------------------

            BuildDstLookup(dstRoot.transform, out var dstByPath, out var dstByName);

            foreach (var srcSmr in srcSmrs)
            {
                if (srcSmr == null)
                {
                    continue;
                }

                // まず「同パス」を優先して探す
                string rel = AnimationUtility.CalculateTransformPath(srcSmr.transform, srcRoot.transform);

                if (!string.IsNullOrEmpty(rel) && dstByPath.TryGetValue(rel, out var dstT) && dstT != null)
                {
                    CopySmrBlendShapesOnly(srcSmr, dstT);
                    continue;
                }

                // 同パスで見つからない場合は “同名” を全探索して適用する
                if (!dstByName.TryGetValue(srcSmr.gameObject.name, out var matches) || matches == null || matches.Count == 0)
                {
                    continue;
                }

                foreach (var t in matches)
                {
                    CopySmrBlendShapesOnly(srcSmr, t);
                }
            }
        }

        /// <summary>
        /// dstRoot 配下の Transform を「相対パス」「同名リスト」向けにまとめて索引化します。
        /// </summary>
        private static void BuildDstLookup(
            Transform dstRoot,
            out Dictionary<string, Transform> byPath,
            out Dictionary<string, List<Transform>> byName
        )
        {
            byPath = new Dictionary<string, Transform>(StringComparer.Ordinal);
            byName = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);

            if (dstRoot == null)
            {
                return;
            }

            var all = dstRoot.GetComponentsInChildren<Transform>(true);

            foreach (var t in all)
            {
                if (t == null)
                {
                    continue;
                }

                // 相対パス（dstRoot 基準）
                string rel = AnimationUtility.CalculateTransformPath(t, dstRoot);
                if (!string.IsNullOrEmpty(rel) && !byPath.ContainsKey(rel))
                {
                    byPath.Add(rel, t);
                }

                // 名前
                if (!byName.TryGetValue(t.name, out var list))
                {
                    list = new List<Transform>();
                    byName.Add(t.name, list);
                }

                list.Add(t);
            }
        }

        private static void CopySmrBlendShapesOnly(SkinnedMeshRenderer srcSmr, Transform dstT)
        {
            if (dstT == null)
            {
                return;
            }

            var dstSmr = dstT.GetComponent<SkinnedMeshRenderer>();
            if (dstSmr == null)
            {
                return;
            }

            CopyBlendShapeWeightsOnly(srcSmr, dstSmr, dstT.name);
        }

        /// <summary>
        /// SkinnedMeshRenderer の BlendShapeWeight（ブレンドシェイプのウェイト）だけをコピーします。
        ///
        /// 仕様：
        /// - “宛先メッシュの BlendShape 名” を基準に、同名がソースに存在する場合のみ上書き
        /// - 存在しない BlendShape は現状維持（0に戻したりしない）
        ///
        /// 実装メモ：
        /// - PrefabInstance 上でも override として残ることを優先し、可能なら SerializedObject 経由で
        ///   m_BlendShapeWeights を直接書き換えます（Scene YAML に Array.data として残りやすい）
        /// - m_BlendShapeWeights が取れない場合は SetBlendShapeWeight をフォールバックとして使用します
        /// </summary>
        public static void CopyBlendShapeWeightsOnly(SkinnedMeshRenderer srcSmr, SkinnedMeshRenderer dstSmr, string dstNameForLog)
        {
            if (srcSmr == null || dstSmr == null)
            {
                return;
            }

            var srcMesh = srcSmr.sharedMesh;
            var dstMesh = dstSmr.sharedMesh;

            if (srcMesh == null || dstMesh == null)
            {
                return;
            }

            var srcWeightByName = new Dictionary<string, float>();
            int srcCount = srcMesh.blendShapeCount;
            for (int i = 0; i < srcCount; i++)
            {
                string name = srcMesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (!srcWeightByName.ContainsKey(name))
                {
                    srcWeightByName.Add(name, srcSmr.GetBlendShapeWeight(i));
                }
            }

            if (srcWeightByName.Count == 0)
            {
                return;
            }

            Undo.RecordObject(dstSmr, L("Undo.SyncBlendShapes"));

            // まずは SerializedObject で m_BlendShapeWeights を直接編集
            try
            {
                var so = new SerializedObject(dstSmr);
                so.Update();

                var weightsProp = so.FindProperty("m_BlendShapeWeights");
                if (weightsProp != null && weightsProp.isArray)
                {
                    int dstCount = dstMesh.blendShapeCount;

                    // 配列が足りない場合だけ拡張（縮めない）
                    if (weightsProp.arraySize < dstCount)
                    {
                        weightsProp.arraySize = dstCount;
                    }

                    // 宛先メッシュの index を基準に “同名がソースにあれば” 上書き
                    for (int dstIndex = 0; dstIndex < dstCount; dstIndex++)
                    {
                        string dstName = dstMesh.GetBlendShapeName(dstIndex);
                        if (string.IsNullOrEmpty(dstName))
                        {
                            continue;
                        }

                        if (!srcWeightByName.TryGetValue(dstName, out float w))
                        {
                            continue;
                        }

                        var elem = weightsProp.GetArrayElementAtIndex(dstIndex);
                        if (elem != null)
                        {
                            elem.floatValue = w;
                        }
                    }

                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(dstSmr);
                    EditorUtility.SetDirty(dstSmr);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(F("Warning.SerializedBlendshapeCopyFailed", dstNameForLog, ex.Message));
            }

            // フォールバック：SetBlendShapeWeight を使う
            try
            {
                int dstCount2 = dstMesh.blendShapeCount;
                for (int dstIndex = 0; dstIndex < dstCount2; dstIndex++)
                {
                    string dstName = dstMesh.GetBlendShapeName(dstIndex);
                    if (string.IsNullOrEmpty(dstName))
                    {
                        continue;
                    }

                    if (!srcWeightByName.TryGetValue(dstName, out float w))
                    {
                        continue;
                    }

                    dstSmr.SetBlendShapeWeight(dstIndex, w);
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(dstSmr);
                EditorUtility.SetDirty(dstSmr);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(F("Warning.BlendshapeCopyFailed", dstNameForLog, ex.Message));
            }
        }

        /// <summary>
        /// BlendShape の “値（ウェイト）” は表示せず、どの SMR のどの BlendShape 名を同期したかだけをログに残します。
        /// </summary>
        public static void CopySkinnedMeshRenderersBlendShapesOnlyWithLog(GameObject srcRoot, GameObject dstRoot, List<string> logs)
        {
            // 実処理（既存ロジックをそのまま使用）
            CopySkinnedMeshRenderersBlendShapesOnly(srcRoot, dstRoot);

            // ログが不要ならここで終了
            if (logs == null)
            {
                return;
            }

            var log = new OCTConversionLogger(logs);

            var stats = CollectBlendShapeNameStats(srcRoot, dstRoot);

            logs.Add(F("Log.BlendshapeSyncSummary", stats.RendererPairs, stats.TotalBlendShapeNames));

            if (stats.Items == null || stats.Items.Count == 0)
            {
                logs.Add(L("Log.BlendshapeNoRenderer"));
                return;
            }

            foreach (var item in stats.Items)
            {
                // ここでは “名前だけ” を表示（値は表示しない）
                log.AddBlendshapeRendererDetail(item.RendererPath, item.BlendShapeNames);
            }
        }

        private struct BlendShapeNameStats
        {
            public int RendererPairs;
            public int TotalBlendShapeNames;
            public List<RendererBlendShapeInfo> Items;
        }

        private struct RendererBlendShapeInfo
        {
            public string RendererPath;
            public List<string> BlendShapeNames;
        }

        private static BlendShapeNameStats CollectBlendShapeNameStats(GameObject srcRoot, GameObject dstRoot)
        {
            var stats = new BlendShapeNameStats
            {
                RendererPairs = 0,
                TotalBlendShapeNames = 0,
                Items = new List<RendererBlendShapeInfo>()
            };

            if (srcRoot == null || dstRoot == null)
            {
                return stats;
            }

            var srcSmrs = srcRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (srcSmrs == null || srcSmrs.Length == 0)
            {
                return stats;
            }

            BuildDstLookup(dstRoot.transform, out var dstByPath, out var dstByName);

            foreach (var srcSmr in srcSmrs)
            {
                if (srcSmr == null)
                {
                    continue;
                }

                var srcMesh = srcSmr.sharedMesh;
                if (srcMesh == null)
                {
                    continue;
                }

                // 既存仕様と同じ優先順位で dst を探す（同パス→同名）
                string rel = AnimationUtility.CalculateTransformPath(srcSmr.transform, srcRoot.transform);

                SkinnedMeshRenderer dstSmr = null;

                if (!string.IsNullOrEmpty(rel) && dstByPath.TryGetValue(rel, out var dstT) && dstT != null)
                {
                    dstSmr = dstT.GetComponent<SkinnedMeshRenderer>();
                }

                if (dstSmr == null && dstByName.TryGetValue(srcSmr.name, out var candidates) && candidates != null)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var t = candidates[i];
                        if (t == null)
                        {
                            continue;
                        }

                        var tmp = t.GetComponent<SkinnedMeshRenderer>();
                        if (tmp != null)
                        {
                            dstSmr = tmp;
                            break;
                        }
                    }
                }

                if (dstSmr == null)
                {
                    continue;
                }

                var dstMesh = dstSmr.sharedMesh;
                if (dstMesh == null)
                {
                    continue;
                }

                var names = GetIntersectingBlendShapeNames(srcMesh, dstMesh);

                stats.RendererPairs++;
                stats.TotalBlendShapeNames += names.Count;

                string pathForLog = string.IsNullOrEmpty(rel) ? dstRoot.name : $"{dstRoot.name}/{rel}";
                stats.Items.Add(new RendererBlendShapeInfo
                {
                    RendererPath = pathForLog,
                    BlendShapeNames = names
                });
            }

            return stats;
        }

        private static List<string> GetIntersectingBlendShapeNames(Mesh srcMesh, Mesh dstMesh)
        {
            var result = new List<string>();

            if (srcMesh == null || dstMesh == null)
            {
                return result;
            }

            var srcNames = new HashSet<string>(StringComparer.Ordinal);
            int srcCount = srcMesh.blendShapeCount;
            for (int i = 0; i < srcCount; i++)
            {
                srcNames.Add(srcMesh.GetBlendShapeName(i));
            }

            int dstCount = dstMesh.blendShapeCount;
            for (int i = 0; i < dstCount; i++)
            {
                string name = dstMesh.GetBlendShapeName(i);
                if (srcNames.Contains(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }
    }
}
#endif
