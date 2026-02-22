#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - おちびちゃんズ変換の「元アバター複製→複製先へ同期適用」変換パイプラインです
// - UI から処理部分を分離し、機能の見通しを良くします
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 変換は必ず複製先に対して行い、元のアバターは触りません
// - Prefab は LoadPrefabContents で展開し、元アセットは書き換えません
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 変換手順の順序は仕様なので、変更時はログ文言も合わせて更新する
// - Undo を必ず記録し、ユーザーが戻せる状態を維持する
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// おちびちゃんズ変換の複製・同期処理をまとめた変換パイプラインです。
    /// </summary>
    internal static class OCTConversionPipeline
    {
        private const string DuplicatedNameSuffix = " (Ochibi-chans)";
        /// <summary>
        /// ローカライズ文字列を取得します。
        /// </summary>
        private static string L(string key) => OCTLocalization.Get(key);

        /// <summary>
        /// ローカライズ文字列をフォーマットして取得します。
        /// </summary>
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        // --------------------------------------------------------------------
        // 処理の全体像（初心者向け）
        // --------------------------------------------------------------------
        // 1) 入力検証（sourceChibiPrefab / sourceTarget の妥当性を確認）
        // 2) 元アバター複製（Ctrl+D 相当）
        // 3) （任意）MA BoneProxy 補正
        // 4) Blueprint ID クリア
        // 5) 変換元 Prefab 展開（読み取り専用）
        //    5-1) FX Controller を取得
        //    5-2) Expressions Menu / Parameters を取得
        //    5-3) Ex AddMenu の PrefabAsset / 親パス / ローカル姿勢を取得
        // 6) 複製先へ同期適用（コア処理）
        //    6-1) ルート localScale 同期
        //    6-2) Armature Transform 同期（位置/回転/スケール）
        //    6-3) Armature 配下の不足 Component 追加 + SerializedObject 参照リマップ
        //    6-4) SkinnedMeshRenderer: BlendShape ウェイトのみ同期
        //    6-5) Ex AddMenu Prefab 追加（未配置時のみ）
        // 7) VRC Descriptor 同期 + MA 衣装調整
        //    7-1) FX / Expressions / ViewPosition を同期
        //    7-2) （任意）Modular Avatar 衣装スケール調整
        // 8) 完了（複製先を選択。適用成功時のみ呼出元後段で元アバターを非アクティブ化）
        // --------------------------------------------------------------------
        /// <summary>
        /// 変換元 Prefab を基準に、対象アバターを複製してから複製先へ同期適用します。
        /// </summary>
        public static bool DuplicateThenApply(
            GameObject sourceChibiPrefab,
            GameObject sourceTarget,
            bool applyMaboneProxyProcessing,
            List<string> logs
        )
        {
            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);

            logs.Add(L("Log.Header.Main"));
            logs.Add(F("Log.ToolVersion", L("Tool.Name"), OCTEditorConstants.ToolVersion));
            logs.Add(F("Log.UnityVersion", Application.unityVersion));
            logs.Add(F("Log.VrcSdkVersion", GetVrcSdkVersionInfo()));
            logs.Add(F("Log.ExecutionTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            logs.Add("");

            logs.Add(F("Log.SourcePrefab", sourceChibiPrefab?.name ?? L("Log.NullValue"), AssetDatabase.GetAssetPath(sourceChibiPrefab)));
            if (sourceTarget != null)
            {
                logs.Add(F("Log.SourceAvatar", OCTConversionLogFormatter.GetHierarchyPath(sourceTarget.transform)));
            }

            logs.Add("");
            log.AddStep(
                "Flow",
                L("Log.Step.Flow.Title"),
                L("Log.Step.Flow.Detail1"),
                L("Log.Step.Flow.Detail2"),
                L("Log.Step.Flow.Detail3"),
                L("Log.Step.Flow.Detail4")
            );

            logs.Add("");
            // ------------------------------------------------------------
            // SVG 対応ステップ: 1) 入力検証（sourceChibiPrefab / sourceTarget）
            // 重要: 必ず「複製先」に対して同期適用。複製失敗時は以降を実行しない。
            // ------------------------------------------------------------

            if (sourceChibiPrefab == null || !EditorUtility.IsPersistent(sourceChibiPrefab))
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ConversionTitle"),
                    L("Dialog.InvalidSourcePrefab"),
                    L("Dialog.Ok")
                );
                return false;
            }

            if (sourceTarget == null)
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ConversionTitle"),
                    L("Dialog.InvalidTargets"),
                    L("Dialog.Ok")
                );
                return false;
            }

            // Undo をまとめる（複製 + 反映 を 1 回の Undo で戻せる方が扱いやすい）
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(L("Undo.DuplicateApply"));

            try
            {
                // --------------------------------------------------------
                // SVG 対応ステップ: 2) 元アバター複製（Ctrl+D 相当）
                // --------------------------------------------------------
                var duplicatedTargets = OCTDuplicateLikeCtrlDHandler.Duplicate(
                    new[] { sourceTarget },
                    restorePreviousSelection: false,
                    renameRule: duplicated => GameObjectUtility.GetUniqueNameForSibling(
                        duplicated != null ? duplicated.transform.parent : null,
                        BuildDuplicateNameWithSuffix(sourceTarget.name)
                    )
                );

                if (duplicatedTargets == null || duplicatedTargets.Length == 0)
                {
                    // ここで終了：複製に失敗しているので “変換はしない”
                    Debug.LogError(L("Error.DuplicateFailed"));
                    logs.Add(L("Log.Error.DuplicateFailed"));
                    return false;
                }

                // 複製先を選択しておく（Ctrl+D と同様の体験）
                Selection.objects = duplicatedTargets;

                log.AddStep(
                    "2",
                    L("Log.Step.2.Title"),
                    L("Log.Step.2.Detail1")
                );
                logs.Add(L("Log.DuplicateSuccess"));
                foreach (var d in duplicatedTargets.Where(x => x != null))
                {
                    logs.Add(F("Log.DuplicateTarget", OCTConversionLogFormatter.GetHierarchyPath(d.transform)));
                }

                logs.Add("");

                // --------------------------------------------------------
                // SVG 対応ステップ: 3) （任意）MA BoneProxy 補正
                // --------------------------------------------------------
                if (applyMaboneProxyProcessing)
                {
                    log.AddStep(
                        "3",
                        L("Log.Step.3.Title"),
                        L("Log.Step.3.Detail1")
                    );
                    logs.Add(L("Log.MaboneProxyHeader"));

                    // MA依存処理の実行可否は Utility の公開状態を参照してここで1回だけ判定します。
                    // ※ ProcessBoneProxies 側では重複判定しない（責務重複を避ける）。
                    if (!OCTModularAvatarUtility.IsModularAvatarAvailable)
                    {
                        logs.Add(L("Log.MaboneProxySkipped"));
                    }
                    else
                    {
                        foreach (var duplicated in duplicatedTargets.Where(x => x != null))
                        {
                            logs.Add(F("Log.TargetEntry", OCTConversionLogFormatter.GetHierarchyPath(duplicated.transform)));
                            OCTModularAvatarUtility.ProcessBoneProxies(duplicated, logs);
                        }
                    }

                    logs.Add("");
                }

                // --------------------------------------------------------
                // SVG 対応ステップ: 4) Blueprint ID クリア
                // --------------------------------------------------------
                log.AddStep(
                    "4",
                    L("Log.Step.4.Title"),
                    L("Log.Step.4.Detail1")
                );
                logs.Add(L("Log.BlueprintClearHeader"));
                foreach (var duplicated in duplicatedTargets.Where(x => x != null))
                {
                    OCTVrcAvatarDescriptorUtility.ClearPipelineBlueprintId(duplicated, logs);
                }
                logs.Add("");

                // --------------------------------------------------------
                // 複製先へ変換を適用
                // --------------------------------------------------------
                var applySucceeded = ApplyConversionToTargets(sourceChibiPrefab, duplicatedTargets, logs: logs);
                return applySucceeded;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>
        /// 重複した接尾辞を避けながら複製先オブジェクト名を決定します。
        /// </summary>
        private static string BuildDuplicateNameWithSuffix(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return DuplicatedNameSuffix.TrimStart();
            }

            var normalizedSourceName = sourceName.TrimEnd();

            if (normalizedSourceName.EndsWith(DuplicatedNameSuffix, StringComparison.Ordinal))
            {
                normalizedSourceName = normalizedSourceName.Substring(0, normalizedSourceName.Length - DuplicatedNameSuffix.Length).TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(normalizedSourceName))
            {
                return DuplicatedNameSuffix.TrimStart();
            }

            return normalizedSourceName + DuplicatedNameSuffix;
        }

        /// <summary>
        /// 変換元 Prefab の参照を読み取り、複製先へ段階的に同期適用します。
        /// （変換パイプラインの 5〜7 ステップ相当）
        /// </summary>
        private static bool ApplyConversionToTargets(GameObject sourceChibiPrefab, GameObject[] targets, List<string> logs)
        {
            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);
            if (sourceChibiPrefab == null || !EditorUtility.IsPersistent(sourceChibiPrefab))
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ConversionTitle"),
                    L("Dialog.InvalidSourcePrefabApply"),
                    L("Dialog.Ok")
                );
                return false;
            }

            if (targets == null || targets.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ConversionTitle"),
                    L("Dialog.TargetsNotFound"),
                    L("Dialog.Ok")
                );
                return false;
            }

            var basePrefabPath = AssetDatabase.GetAssetPath(sourceChibiPrefab);
            if (string.IsNullOrEmpty(basePrefabPath))
            {
                Debug.LogError(L("Error.SourcePrefabPathMissing"));

                return false;
            }

            GameObject basePrefabRoot = null;

            try
            {
                logs.Add(F("Log.PrefabExpand", basePrefabPath));
                log.AddStep(
                    "5",
                    L("Log.Step.5.Title"),
                    L("Log.Step.5.Detail1"),
                    L("Log.Step.5.Detail2")
                );
                logs.Add("");
                // --------------------------------------------------------
                // SVG 対応ステップ: 5) 変換元 Prefab 展開（読み取り専用）
                // --------------------------------------------------------
                basePrefabRoot = PrefabUtility.LoadPrefabContents(basePrefabPath);

                // --------------------------------------------------------
                // 変換に必要な参照を sourceChibiPrefab の VRCAvatarDescriptor から抽出
                // --------------------------------------------------------
                OCTVrcAvatarDescriptorUtility.TryGetFxPlayableLayerControllerFromBasePrefab(
                    basePrefabRoot,
                    out var fxController
                );

                OCTVrcAvatarDescriptorUtility.TryGetExpressionsMenuAndParametersFromBasePrefab(
                    basePrefabRoot,
                    out var expressionsMenu,
                    out var expressionParameters
                );

                // Ochibichans_Addmenu は sourceChibiPrefab の内部にある想定
                TryResolveExAddMenuPlacementFromSourcePrefab(basePrefabRoot, out var exAddMenuPlacement);

                // --------------------------------------------------------
                // 変換対象へ反映
                // --------------------------------------------------------
                foreach (var dstRoot in targets)
                {
                    if (dstRoot == null)
                    {
                        continue;
                    }

                    logs.Add(L("Log.Separator"));
                    logs.Add(F("Log.TargetEntry", OCTConversionLogFormatter.GetHierarchyPath(dstRoot.transform)));
                    log.AddStep(
                        "6",
                        L("Log.Step.6.Title"),
                        L("Log.Step.6.Detail1")
                    );

                    // 実行前の参照（FX / Menu / Parameters）を取得してログ用に保持（値は出さない）
                    OCTVrcAvatarDescriptorUtility.TryGetFxPlayableLayerControllerFromAvatar(dstRoot, out var fxBefore);
                    OCTVrcAvatarDescriptorUtility.TryGetExpressionsMenuAndParametersFromAvatar(dstRoot, out var menuBefore, out var paramsBefore);

                    logs.Add(F("Log.FxBefore", OCTConversionLogFormatter.FormatAssetRef(fxBefore)));
                    logs.Add(F("Log.MenuBefore", OCTConversionLogFormatter.FormatAssetRef(menuBefore)));
                    logs.Add(F("Log.ParametersBefore", OCTConversionLogFormatter.FormatAssetRef(paramsBefore)));
                    logs.Add("");

                    // SVG 対応ステップ: 6) 複製先へ同期適用（コア処理）
                    ApplyCoreAvatarSynchronization(basePrefabRoot, dstRoot, logs);

                    // Ex AddMenu Prefab 追加（未配置時のみ）
                    if (exAddMenuPlacement.PrefabAsset != null)
                    {
                        logs.Add(L("Log.ExPrefabHeader"));
                        TryAddExPrefabIfMissing(dstRoot, exAddMenuPlacement, logs);
                        logs.Add("");
                    }

                    // SVG 対応ステップ: 7) VRC Descriptor 同期 + MA 衣装調整
                    log.AddStep(
                        "7",
                        L("Log.Step.7.Title"),
                        L("Log.Step.7.Detail1"),
                        L("Log.Step.7.Detail2")
                    );
                    logs.Add("");
                    if (fxController != null)
                    {
                        logs.Add(F("Log.FxApply", OCTConversionLogFormatter.FormatAssetRef(fxController)));
                        OCTVrcAvatarDescriptorUtility.SetFxPlayableLayerController(dstRoot, fxController);
                    }
                    else
                    {
                        logs.Add(L("Log.FxApplySkipped"));
                    }

                    logs.Add("");

                    if (expressionsMenu != null || expressionParameters != null)
                    {
                        logs.Add(F("Log.ExpressionsApply", OCTConversionLogFormatter.FormatAssetRef(expressionsMenu), OCTConversionLogFormatter.FormatAssetRef(expressionParameters)));
                        OCTVrcAvatarDescriptorUtility.SetExpressionsMenuAndParameters(dstRoot, expressionsMenu, expressionParameters);
                    }
                    else
                    {
                        logs.Add(L("Log.ExpressionsApplySkipped"));
                    }

                    logs.Add("");

                    // ViewPosition も sourceChibiPrefab と同じ値へ同期
                    {
                        var viewOk = OCTVrcAvatarDescriptorUtility.TryCopyViewPositionFromBasePrefab(dstRoot, basePrefabRoot);
                        logs.Add(viewOk ? L("Log.ViewPositionApplied") : L("Log.ViewPositionSkipped"));
                    }

                    logs.Add("");

                    // MA 衣装スケール調整（Modular Avatar がある環境のみ実行）
                    if (!OCTModularAvatarUtility.AdjustCostumeScalesForModularAvatarMeshSettings(dstRoot, basePrefabRoot, logs))
                    {
                        logs.Add(L("Log.Error.CostumeScaleFailed"));
                        return false;
                    }

                    // 実行後の参照（FX / Menu / Parameters）
                    OCTVrcAvatarDescriptorUtility.TryGetFxPlayableLayerControllerFromAvatar(dstRoot, out var fxAfter);
                    OCTVrcAvatarDescriptorUtility.TryGetExpressionsMenuAndParametersFromAvatar(dstRoot, out var menuAfter, out var paramsAfter);

                    logs.Add("");
                    logs.Add(F("Log.FxAfter", OCTConversionLogFormatter.FormatAssetRef(fxAfter)));
                    logs.Add(F("Log.MenuAfter", OCTConversionLogFormatter.FormatAssetRef(menuAfter)));
                    logs.Add(F("Log.ParametersAfter", OCTConversionLogFormatter.FormatAssetRef(paramsAfter)));

                    // 差分が分かるように補足（値は出さない）
                    if (!ReferenceEquals(fxBefore, fxAfter))
                    {
                        logs.Add(L("Log.FxChanged"));
                    }

                    if (!ReferenceEquals(menuBefore, menuAfter))
                    {
                        logs.Add(L("Log.MenuChanged"));
                    }

                    if (!ReferenceEquals(paramsBefore, paramsAfter))
                    {
                        logs.Add(L("Log.ParametersChanged"));
                    }

                    logs.Add(L("Log.Separator"));
                    log.AddStep(
                        "8",
                        L("Log.Step.8.Title"),
                        L("Log.Step.8.Detail1")
                    );
                    logs.Add("");
                }

                return true;
            }
            finally
            {
                // 展開した Prefab コンテンツは必ず解放
                if (basePrefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(basePrefabRoot);
                }
            }
        }

        /// <summary>
        /// sourceChibiPrefab（PrefabContentsとして読み込んだ一時ルート）から、
        /// 「Ochibichans_Addmenu.prefab を参照しているネストPrefabインスタンス」を探し、
        /// 追加に必要な情報（Prefabアセット参照 + 位置/回転/スケール + 望ましい親パス）を返します。
        /// </summary>
        private struct ExPrefabPlacement
        {
            public GameObject PrefabAsset;
            public string ParentPathFromAvatarRoot;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        /// <summary>
        /// VRC SDK のバージョン情報を取得して表示用文字列に整形します。
        /// </summary>
        private static string GetVrcSdkVersionInfo()
        {
#if VRC_SDK_VRCSDK3
            var assembly = typeof(VRCAvatarDescriptor).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var version = info?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? L("Log.UnknownVersion");
            return $"{assembly.GetName().Name} {version}";
#else
            return L("Log.NotFound");
#endif
        }

        /// <summary>
        /// 変換元 sourceChibiPrefab 内から Ex AddMenu の配置情報を解決します。
        /// </summary>
        private static bool TryResolveExAddMenuPlacementFromSourcePrefab(GameObject basePrefabRoot, out ExPrefabPlacement placement)
        {
            placement = default;

            if (basePrefabRoot == null)
            {
                return false;
            }

            // ------------------------------------------------------------
            // 仕様：
            // sourceChibiPrefab（= basePrefabRoot）内には、
            // 「Ochibichans_Addmenu.prefab のネスト（Prefabインスタンス）」が含まれている想定です。
            //
            // ここで “重要” なのは、
            //  - basePrefabRoot 上の GameObject（PrefabContents の一時オブジェクト）を
            //    そのままターゲットへ移動/親変更しないこと。
            //    （Prefabインスタンス内の Transform は親変更できず、エラーになります）
            //
            // そのため、必ず「参照している Prefab アセット（Project 上の .prefab）」を見つけて、
            // そのアセットを Instantiate してターゲットに追加する方式にします。
            // ------------------------------------------------------------

            // 期待するファイル名（拡張子まで一致）
            const string targetFileName = OCTEditorConstants.AddMenuPrefabFileName;

            // ------------------------------------------------------------
            // まずは “Prefabアセットパス” から確実に特定します。
            // ネストPrefabのルート名が変更されていても、
            // 参照しているアセットパスが一致すれば発見できます。
            // ------------------------------------------------------------
            foreach (var t in basePrefabRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                {
                    continue;
                }

                var go = t.gameObject;
                if (go == null)
                {
                    continue;
                }

                if (go == basePrefabRoot)
                {
                    continue;
                }

                // ネストPrefabに属している（= Prefabインスタンスの一部）なら、
                // その「最も近いインスタンスルート」を取得できます。
                var instRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (instRoot == null)
                {
                    continue;
                }

                if (instRoot == basePrefabRoot)
                {
                    continue;
                }

                var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instRoot);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                // ファイル名で一致判定（大文字小文字は無視）
                if (!assetPath.EndsWith(targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    continue;
                }

                // --------------------------------------------------------
                // “座標が反映されない” 問題の対策：
                // sourceChibiPrefab 内での「ローカル座標/回転/スケール」を読み取り、
                // Instantiate した後に同じ値を適用します。
                // --------------------------------------------------------
                var instTransform = instRoot.transform;
                placement = new ExPrefabPlacement
                {
                    PrefabAsset = asset,
                    ParentPathFromAvatarRoot = BuildRelativePathFromRoot(basePrefabRoot.transform, instTransform.parent),
                    LocalPosition = instTransform.localPosition,
                    LocalRotation = instTransform.localRotation,
                    LocalScale = instTransform.localScale
                };
                return true;
            }

            // ------------------------------------------------------------
            // フォールバック：オブジェクト名に "Ochibichans_Addmenu" を含むものから辿ります。
            // （Prefabのファイル名が変わっている等のレアケース向け）
            // ------------------------------------------------------------
            foreach (var t in basePrefabRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                {
                    continue;
                }

                var go = t.gameObject;
                if (go == null)
                {
                    continue;
                }

                if (go == basePrefabRoot)
                {
                    continue;
                }

                if (go.name.IndexOf(OCTEditorConstants.AddMenuNameKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var instRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (instRoot == null)
                {
                    continue;
                }

                if (instRoot == basePrefabRoot)
                {
                    continue;
                }

                var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instRoot);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    continue;
                }

                var instTransform = instRoot.transform;
                placement = new ExPrefabPlacement
                {
                    PrefabAsset = asset,
                    ParentPathFromAvatarRoot = BuildRelativePathFromRoot(basePrefabRoot.transform, instTransform.parent),
                    LocalPosition = instTransform.localPosition,
                    LocalRotation = instTransform.localRotation,
                    LocalScale = instTransform.localScale
                };
                return true;
            }

            Debug.LogWarning(L("Warning.AddMenuPrefabMissing"));
            return false;
        }

        /// <summary>
        /// root から target までの相対パスを構築して返します。
        /// </summary>
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

            // root より上に行ってしまう場合は「不正」とみなして空にします。
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

        /// <summary>
        /// 複製先へ同期適用するコア処理（Transform / Component / BlendShape）をまとめて実行します。
        /// </summary>
        private static void ApplyCoreAvatarSynchronization(GameObject srcRoot, GameObject dstRoot, List<string> logs)
        {
            logs ??= new List<string>();

            logs.Add(L("Log.CoreHeader"));
            logs.Add(L("Log.Step.6.Substep1"));
            logs.Add(L("Log.RootScaleApplied"));
            Undo.RecordObject(dstRoot.transform, L("Undo.SyncRootScale"));
            dstRoot.transform.localScale = srcRoot.transform.localScale;
            EditorUtility.SetDirty(dstRoot.transform);

            var srcArmature = OCTEditorUtility.FindAvatarMainArmature(srcRoot.transform);
            if (srcArmature == null)
            {
                logs.Add(L("Log.Step.6.Substep2SkippedSource"));
                return;
            }

            var dstArmature = OCTEditorUtility.FindAvatarMainArmature(dstRoot.transform);
            if (dstArmature == null)
            {
                logs.Add(L("Log.Step.6.Substep2SkippedTarget"));
                return;
            }

            logs.Add(L("Log.Step.6.Substep2"));
            CopyArmatureTransforms(srcArmature, dstArmature, logs);
            logs.Add(L("Log.Step.6.Substep3"));
            AddMissingComponentsUnderArmature(srcRoot, dstRoot, srcArmature, dstArmature, logs);
            logs.Add(L("Log.Step.6.Substep4"));
            OCTSkinnedMeshUtility.CopySkinnedMeshRenderersBlendShapesOnlyWithLog(srcRoot, dstRoot, logs);
        }

        /// <summary>
        /// Armature 配下の Transform 値をパス一致で複製先へ同期します。
        /// </summary>
        private static void CopyArmatureTransforms(Transform srcArmature, Transform dstArmature, List<string> logs)
        {
            if (srcArmature == null || dstArmature == null)
            {
                return;
            }

            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);
            logs.Add(L("Log.ArmatureTransformApplied"));

            var srcAll = srcArmature.GetComponentsInChildren<Transform>(true);
            logs.Add(F("Log.ArmatureTransformScan", srcAll.Length));
            int updated = 0;
            int missingPathCount = 0;
            var updatedPaths = new List<string>();

            foreach (var srcT in srcAll)
            {
                if (srcT == null)
                {
                    continue;
                }

                string rel = AnimationUtility.CalculateTransformPath(srcT, srcArmature);
                rel = OCTEditorUtility.NormalizeRelPathFor(dstArmature, rel);

                var dstT = string.IsNullOrEmpty(rel) ? dstArmature : dstArmature.Find(rel);
                if (dstT == null)
                {
                    missingPathCount++;
                    continue;
                }

                Undo.RecordObject(dstT, L("Undo.SyncArmatureTransform"));
                dstT.localPosition = srcT.localPosition;
                dstT.localRotation = srcT.localRotation;
                dstT.localScale = srcT.localScale;

                EditorUtility.SetDirty(dstT);

                updated++;
                updatedPaths.Add(OCTConversionLogFormatter.GetHierarchyPath(dstT));
            }

            log.AddPathEntries(updatedPaths);

            logs.Add(F("Log.ArmatureTransformPathMissing", missingPathCount));

            logs.Add(F("Log.ArmatureTransformUpdated", updated));
            logs.Add("");
        }

        /// <summary>
        /// 変換元に存在し複製先に不足している Component を追加し、参照も補正します。
        /// </summary>
        private static void AddMissingComponentsUnderArmature(
            GameObject srcRoot,
            GameObject dstRoot,
            Transform srcArmature,
            Transform dstArmature,
            List<string> logs
        )
        {
            if (srcRoot == null || dstRoot == null || srcArmature == null || dstArmature == null)
            {
                return;
            }

            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);
            logs.Add(L("Log.AddMissingComponents"));

            var srcAll = srcArmature.GetComponentsInChildren<Transform>(true);
            logs.Add(F("Log.AddMissingComponentsScan", srcAll.Length));
            int addedCount = 0;
            int missingPathCount = 0;
            int alreadySatisfiedCount = 0;

            foreach (var srcT in srcAll)
            {
                if (srcT == null)
                {
                    continue;
                }

                string rel = AnimationUtility.CalculateTransformPath(srcT, srcArmature);
                rel = OCTEditorUtility.NormalizeRelPathFor(dstArmature, rel);

                var dstT = string.IsNullOrEmpty(rel) ? dstArmature : dstArmature.Find(rel);
                if (dstT == null)
                {
                    missingPathCount++;
                    continue;
                }

                var srcGO = srcT.gameObject;
                var dstGO = dstT.gameObject;

                var srcComps = srcGO.GetComponents<Component>();
                var srcByType = new Dictionary<Type, List<Component>>();

                foreach (var c in srcComps)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    if (c is Transform)
                    {
                        continue;
                    }

                    var type = c.GetType();
                    if (!srcByType.TryGetValue(type, out var list))
                    {
                        list = new List<Component>();
                        srcByType[type] = list;
                    }

                    list.Add(c);
                }

                foreach (var kv in srcByType)
                {
                    var type = kv.Key;
                    var srcList = kv.Value;

                    var dstExisting = dstGO.GetComponents(type);
                    int dstCount = dstExisting != null ? dstExisting.Length : 0;
                    if (dstCount >= srcList.Count)
                    {
                        alreadySatisfiedCount++;
                    }

                    for (int i = 0; i < srcList.Count; i++)
                    {
                        if (i < dstCount)
                        {
                            continue;
                        }

                        Component newComp = null;

                        try
                        {
                            newComp = Undo.AddComponent(dstGO, type);
                        }
                        catch
                        {
                            continue;
                        }

                        if (newComp == null)
                        {
                            continue;
                        }

                        try
                        {
                            EditorUtility.CopySerialized(srcList[i], newComp);
                        }
                        catch
                        {
                            // コピーに失敗した場合は、追加だけ残して処理続行
                        }

                        OCTEditorUtility.RemapObjectReferencesInObject(newComp, srcRoot, dstRoot);
                        EditorUtility.SetDirty(newComp);

                        addedCount++;
                        log.Add("Log.ComponentAdded", type.Name, OCTConversionLogFormatter.GetHierarchyPath(dstT));
                    }
                }
            }

            logs.Add(F("Log.AddMissingComponentsPathMissing", missingPathCount));
            logs.Add(F("Log.AddMissingComponentsAlreadySatisfied", alreadySatisfiedCount));

            logs.Add(F("Log.MissingComponentsAdded", addedCount));
            logs.Add("");
        }

        /// <summary>
        /// Ex AddMenu Prefab が未配置の場合のみ、指定の親配下にインスタンスを追加します。
        /// </summary>
        private static bool TryAddExPrefabIfMissing(GameObject avatarRoot, ExPrefabPlacement placement, List<string> logs)
        {
            if (avatarRoot == null)
            {
                return false;
            }

            if (placement.PrefabAsset == null)
            {
                return false;
            }

            logs ??= new List<string>();
            var prefabPath = AssetDatabase.GetAssetPath(placement.PrefabAsset);

            // 既に同じ EX プレハブ（同じPrefabアセット由来のインスタンス）が
            // avatarRoot 配下のどこかに存在するなら、重複追加しません。
            if (ContainsPrefabInstanceInDescendants(avatarRoot, placement.PrefabAsset))
            {
                logs.Add(F("Log.ExPrefabAlreadyExists", placement.PrefabAsset.name, prefabPath));
                return false;
            }

            var instanceObj = PrefabUtility.InstantiatePrefab(placement.PrefabAsset) as GameObject;
            if (instanceObj == null)
            {
                logs.Add(F("Log.ExPrefabCreationFailed", placement.PrefabAsset.name, prefabPath));
                return false;
            }

            // “どこにぶら下げるか” は sourceChibiPrefab の配置を優先します。
            // ただし、ターゲット側で同じパスが見つからない場合はアバタールート直下にフォールバックします。
            Transform parentTransform = avatarRoot.transform;
            if (!string.IsNullOrEmpty(placement.ParentPathFromAvatarRoot))
            {
                var foundParent = avatarRoot.transform.Find(placement.ParentPathFromAvatarRoot);
                if (foundParent != null)
                {
                    parentTransform = foundParent;
                }
            }

            // Undo で「生成」と「親付け替え」を記録（Ctrl+Z 対応）
            Undo.RegisterCreatedObjectUndo(instanceObj, L("Undo.AddExPrefab"));
            Undo.SetTransformParent(instanceObj.transform, parentTransform, L("Undo.AddExPrefab"));

            // “座標が反映されない” 問題の対策：
            // sourceChibiPrefab 内でのローカル姿勢を、そのまま複製して適用します。
            // ※ログでは値は出しません
            Undo.RecordObject(instanceObj.transform, L("Undo.AddExPrefab"));
            instanceObj.transform.localPosition = placement.LocalPosition;
            instanceObj.transform.localRotation = placement.LocalRotation;
            instanceObj.transform.localScale = placement.LocalScale;

            EditorUtility.SetDirty(instanceObj);

            logs.Add(F("Log.ExPrefabAdded", placement.PrefabAsset.name, prefabPath));
            logs.Add(F("Log.ExPrefabParent", OCTConversionLogFormatter.GetHierarchyPath(parentTransform)));
            logs.Add(F("Log.ExPrefabAddedTo", OCTConversionLogFormatter.GetHierarchyPath(instanceObj.transform)));
            logs.Add(L("Log.ExPrefabTransformApplied"));

            return true;
        }

        /// <summary>
        /// parent 配下に対象 Prefab のインスタンスが存在するかを判定します。
        /// </summary>
        private static bool ContainsPrefabInstanceInDescendants(GameObject parent, GameObject prefabAssetRoot)
        {
            if (parent == null || prefabAssetRoot == null)
            {
                return false;
            }

            // ------------------------------------------------------------
            // 既に同じ Prefab アセット由来のインスタンスが存在するか？
            //
            // 以前は GetCorrespondingObjectFromSource の “参照一致” で判定していましたが、
            // - prefabAssetRoot が「ルートGameObject」ではなく “Prefab内の子オブジェクト” だった
            // - ネストPrefab / Variant などで参照が取りにくい
            // といったケースで誤判定が起きやすいです。
            //
            // そこで、より確実に「Prefabアセットのパス」で比較します。
            // ------------------------------------------------------------
            var targetAssetPath = AssetDatabase.GetAssetPath(prefabAssetRoot);
            if (string.IsNullOrEmpty(targetAssetPath))
            {
                // ここが空なら「Project上のアセット」ではない可能性が高い（安全のため重複判定しない）
                return false;
            }

            // parent 自身は除外して “子孫” を検索
            var descendants = parent.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < descendants.Length; i++)
            {
                var t = descendants[i];
                if (t == null)
                {
                    continue;
                }

                if (t == parent.transform)
                {
                    continue;
                }

                // Prefabインスタンスの “ルート” だけを対象にする（子要素は判定がブレるため）
                var instRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
                if (instRoot != t.gameObject)
                {
                    continue;
                }

                var instAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instRoot);
                if (string.IsNullOrEmpty(instAssetPath))
                {
                    continue;
                }

                if (string.Equals(instAssetPath, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ログ用ユーティリティは OCTConversionLogFormatter に切り出し
    }
}
#endif
