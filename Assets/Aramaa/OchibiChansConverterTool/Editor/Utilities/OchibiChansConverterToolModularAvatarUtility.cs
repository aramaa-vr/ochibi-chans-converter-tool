#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OchibiChansConverterToolModularAvatarUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - Modular Avatar（MA Mesh Settings）を使っている衣装に対して、複製先のボーン localScale を同期します
// - 衣装が親（アバター）のスケールに追従できず破綻するケースを減らす目的です
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - Modular Avatar が入っていないプロジェクトでは何もしません（安全にスキップ）
// - 対象は「MA Mesh Settings が付いているオブジェクト」を衣装ルートと見なします
// - 変更ログは呼び出し側へ返します（メインウィンドウに表示しないため）
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
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// Modular Avatar の Mesh Settings が付いた衣装に対して、複製先のスケール・BlendShape を同期します。
    /// </summary>
    internal static class OchibiChansConverterToolModularAvatarUtility
    {
        private const float ScaleEpsilon = 0.0001f;

        private static string L(string key) => OchibiChansConverterToolLocalization.Get(key);
        private static string F(string key, params object[] args) => OchibiChansConverterToolLocalization.Format(key, args);

        /// <summary>
        /// dstRoot（複製先）配下で “MA Mesh Settings が付いた衣装ルート” を探し、衣装スケール調整を実行します。
        /// </summary>
        public static bool AdjustCostumeScalesForModularAvatarMeshSettings(
            GameObject dstRoot,
            GameObject basePrefabRoot,
            List<string> logs = null
        )
        {
            if (dstRoot == null || basePrefabRoot == null)
            {
                return false;
            }

            // アバター（変換先）側の Armature を基準に “スケール差分のあるボーン一覧” を作る
            var dstArmature = OchibiChansConverterToolEditorUtility.FindAvatarMainArmature(dstRoot.transform);

            if (dstArmature == null)
            {
                return false;
            }

            var baseArmaturePaths = BuildBaseArmatureTransformPaths(basePrefabRoot, logs);

            var avatarBoneScaleModifiers = BuildAvatarBoneScaleModifiers(dstArmature, baseArmaturePaths, logs);

            if (avatarBoneScaleModifiers.Count == 0)
            {
                return true;
            }

            // 衣装ルート候補：MA Mesh Settings を持つ GameObject を抽出
            // 衣装のみ処理を行うため、親のArmature（dstRoot自身）を処理対象から除外する
            List<Transform> costumeRoots;
#if CHIBI_MODULAR_AVATAR
            costumeRoots = dstRoot.GetComponentsInChildren<ModularAvatarMeshSettings>(true)
                .Select(c => c != null ? c.transform : null)
                .Where(t => t != null && t.gameObject != dstRoot)
                .Distinct()
                .ToList();
#else
            // Modular Avatar が入っていない環境では安全にスキップ
            logs?.Add(L("Log.ModularAvatarMissing"));
            return true;
#endif

            if (costumeRoots.Count == 0)
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

        /// <summary>
        /// Base Prefab 側の Armature を基準に「許可する Transform パス」を作ります。
        /// </summary>
        private static HashSet<string> BuildBaseArmatureTransformPaths(GameObject basePrefabRoot, List<string> logs)
        {
            if (basePrefabRoot == null)
            {
                return null;
            }

            // Base Prefab 側の Armature を基準に「許可する Transform パス」を作ります。
            // これにより、元アバターの Armature 配下に追加したアクセサリ（眼鏡・ヘッドホン等）の
            // “独自Armature” や “拡大縮小した Transform” が、衣装スケール調整に混入する事故を防げます。
            var baseArmature = OchibiChansConverterToolEditorUtility.FindAvatarMainArmature(basePrefabRoot.transform);

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
                if (b == null)
                {
                    continue;
                }

                // “1,1,1 以外” のボーンだけを対象とする
                if (IsNearlyOne(b.localScale))
                {
                    continue;
                }

                // アクセサリ等の “追加階層” を拾ってしまうと衣装スケールが破綻するため、
                // Base Prefab の Armature に存在する Transform パスだけを対象にします。
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

                // 同名が複数あっても、最初に見つかったものを採用（既存互換）
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

        // --------------------------------------------------------------------
        // 服（衣装）側のボーン localScale を調整する
        //
        // 【重要】この処理は、既存の運用実績に基づくマッチング順序を踏襲します。
        //
        // - avatarBoneScaleModifiers の各キー（アバター側でスケールが 1,1,1 ではないボーン名）について
        // - 1) 文字列完全一致 → 2) Armatureパス一致 → 3) 部分一致 の順で最初のボーンを探す
        // - bone.localScale = Vector3.Scale(bone.localScale, modifier.Scale) を 1回だけ適用（= 乗算補正）
        // - 適用したボーンはリストから除外して、同じキーが複数のボーンに二重適用されないようにする
        //
        // ※「上書き（= avatar の localScale をそのまま代入）」にしてしまうと、
        //    既存の衣装側スケール設計が崩れるため NG です。
        // --------------------------------------------------------------------
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

            // 服（衣装）側の Transform を変更するので、衣装ルートを Undo 対象に登録しておきます。
            // これにより、変換後に Ctrl+Z で元に戻せます。
            Undo.RegisterFullObjectHierarchyUndo(costumeRoot.gameObject, L("Undo.AdjustCostumeScales"));
            if (avatarBoneScaleModifiers == null || avatarBoneScaleModifiers.Count == 0)
            {
                return;
            }

            int appliedCount = 0;

            // 衣装配下の Transform 一覧（ボーンも含む）
            // Remove しながら回すため List 化します（既存実装の挙動に合わせる）。
            var costumeBones = costumeRoot.GetComponentsInChildren<Transform>(true).ToList();
            var costumeArmature = OchibiChansConverterToolEditorUtility.FindAvatarMainArmature(costumeRoot);

            foreach (var modifier in avatarBoneScaleModifiers)
            {
                // 既存実装に合わせて、列挙対象は “現在のリスト参照” をそのまま使います。
                // （途中で costumeBones.Remove(bone) を行うため、意図を明示しています）
                var temp = costumeBones;

                // まずは「完全一致」で探す（誤マッチ防止）
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

                // 2) Armature 比較（ヒューマノイドの骨構造比較に近い形でパス一致を見る）
                if (!string.IsNullOrEmpty(modifier.RelativePath) && costumeArmature != null)
                {
                    var normalizedPath = OchibiChansConverterToolEditorUtility.NormalizeRelPathFor(costumeArmature, modifier.RelativePath);
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

                // 3) 互換性維持のため、完全一致＆Armature一致が無い場合のみ Contains を許可
                // （ただし誤マッチしやすいため、今後は段階的に縮小する前提）
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
            List<Transform> bones,
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
                if (bone == null)
                {
                    continue;
                }

                if (!predicate(bone))
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

            // ★ここが要点：既存実装どおり “上書き” ではなく “乗算” で補正する
            bone.localScale = Vector3.Scale(bone.localScale, scaleModifier);

            EditorUtility.SetDirty(bone);
            appliedCount++;

            if (logs != null)
            {
                logs.Add(F(
                    "Log.CostumeScaleApplied",
                    costumeRoot?.name ?? L("Log.NullValue"),
                    modifierKey,
                    matchLabel,
                    GetTransformPath(bone, costumeRoot)));
            }

            // 1キーにつき 1ボーンだけ補正して次へ（既存挙動）
            removalTarget?.Remove(bone);
            return true;
        }

        // --------------------------------------------------------------------
        // 追加機能：衣装スケール調整時に BlendShape も同期する
        //
        // 目的：
        // - MA Mesh Settings 配下の衣装は、スケール調整だけだと見た目が破綻するケースがあります。
        // - その一因として「衣装側 SMR の BlendShape がアバター側（基準Prefab）と揃っていない」場合があるため、
        //   ここで “同名 + 同メッシュ” の BlendShape を同期します。
        //
        // 注意：
        // - ログには BlendShape の「値」は出しません（ユーザー要望）。
        // - 代わりに、対象 SMR の階層パスと BlendShape 名をすべて列挙します。
        // --------------------------------------------------------------------
        private static Dictionary<string, float> BuildBaseBlendShapeWeightsByMeshAndName(GameObject basePrefabRoot, List<string> logs)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (basePrefabRoot == null)
            {
                return map;
            }

            var smrs = basePrefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // 衣装同期の基準は、なるべく Body_base（体型のBlendShapeを持つことが多い）を優先します。
            // 同じBlendShape名が複数のSMRに存在する場合でも、最初に登録された値を採用する方針のため、
            // ここで走査順を調整しておきます。
            smrs = smrs
                .OrderByDescending(s => GetBaseSmrPriority(s))
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
                        // 万一ここで例外が出ても、同期機能は補助なので安全にスキップします。
                        continue;
                    }

                    var key = MakeBlendShapeKey(mesh, shapeName);

                    // 同じ key（同メッシュ+同名）が複数あっても、最初に見つかったものを採用（既存互換）
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

                // 「どの BlendShape が存在するか」を全列挙（= 全確認）
                var toApplyIndices = new List<int>();
                var toApplyNames = new List<string>();

                for (int i = 0; i < count; i++)
                {
                    var shapeName = mesh.GetBlendShapeName(i);
                    logs?.Add(F("Log.BlendshapeEntry", shapeName));

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

                // 同期対象が無ければ、このSMRは確認のみ
                if (toApplyIndices.Count == 0)
                {
                    continue;
                }

                // 変更する SMR の Undo を記録（ユーザーが戻せることを最優先）
                Undo.RecordObject(smr, L("Undo.OchibiChansConverterToolSyncBlendShapes"));

                for (int k = 0; k < toApplyIndices.Count; k++)
                {
                    int idx = toApplyIndices[k];
                    string shapeName = toApplyNames[k];

                    var key = MakeBlendShapeKey(mesh, shapeName);
                    var weight = baseBlendShapeWeights[key];

                    // 値そのものはログに出さない（ユーザー要望）
                    smr.SetBlendShapeWeight(idx, weight);

                    // PrefabInstance 等でも反映が残りやすいよう、可能なら SerializedProperty も更新
                    TrySetBlendShapeWeightSerialized(smr, idx, weight);
                }

                EditorUtility.SetDirty(smr);

                // Prefab Instance の差分として残りやすくする（Scene上のオブジェクトのみ）
                PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
                logs?.Add(F("Log.BlendshapeSynced", string.Join(", ", toApplyNames)));
            }
        }

        private static string MakeBlendShapeKey(Mesh mesh, string shapeName)
        {
            // 服（衣装）側のメッシュ名はベースと一致しないことが多い（例: コルセット等）ため、
            // 「同メッシュ名 + 同BlendShape名」での一致は成立しにくいです。
            // そのため、衣装同期では "BlendShape名" のみで照合します（大小文字は区別しない）。
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

            // 優先度は「Mesh名」「GameObject名」両方を見る（環境差吸収）
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

        /// <summary>
        /// Transform パスを「同名兄弟があっても衝突しにくい」形で生成します。
        ///
        /// - CalculateTransformPath は同名の兄弟があると曖昧になり得ます（Find の結果が不定になりやすい）。
        /// - ここでは「同名の兄弟の中で何番目か（0-based）」をパスに含め、安定した比較に使います。
        /// - ログ表示はユーザーが読みやすいよう、別途 GetTransformPath（人間向け）を使います。
        /// </summary>
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

            // root まで遡れない場合でも落ちないようにする（比較用途としては target.name を返す）
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
                    // root より上まで来てしまった（想定外の階層）
                    segments.Add(current.name);
                    break;
                }

                int ordinal = 0;
                int sameNameCount = 0;

                // 同名の兄弟の数と、その中での順番を算出
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    if (!string.Equals(child.name, current.name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (child == current)
                    {
                        ordinal = sameNameCount;
                    }

                    sameNameCount++;
                }

                // 同名が複数あるときだけ suffix を付ける（普段は見た目を変えない）
                if (sameNameCount > 1)
                {
                    segments.Add($"{current.name}#{ordinal}");
                }
                else
                {
                    segments.Add(current.name);
                }

                current = parent;
            }

            segments.Reverse();

            // root.name が含まれていない場合でも比較ができるよう join
            return string.Join("/", segments);
        }

        /// <summary>
        /// ルートからの階層パスを返します（ログ用途）。
        /// </summary>
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

            // target == root の場合、CalculateTransformPath は空文字を返す
            var rel = AnimationUtility.CalculateTransformPath(target, root);
            if (string.IsNullOrEmpty(rel))
            {
                return root.name;
            }

            return root.name + "/" + rel;
        }

        /// <summary>
        /// SetBlendShapeWeight だけだと、環境によっては Editor 上での永続化が不安定になるケースがあるため、
        /// 可能な場合は SerializedObject でも同じ値を書き込みます（ログに値は出さない）。
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

                // Undo は既に RecordObject 済みなので、ここでは Undo を追加しない
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            catch
            {
                // 失敗しても SetBlendShapeWeight は適用済みなので安全に無視
            }
        }
    }
}
#endif
