#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTVrcAvatarDescriptorUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - VRCAvatarDescriptor の設定（FX / Expressions / ViewPosition）を、複製先へ安全に同期します
// - VRChat SDK が導入されている前提で、VRCAvatarDescriptor を直接操作します
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - VRChat SDK の型を直接参照します
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

using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// VRCAvatarDescriptor の設定を安全に読み書きし、変換元 Prefab → 複製先の同期に使うユーティリティです。
    /// </summary>
    internal static class OCTVrcAvatarDescriptorUtility
    {
        /// <summary>
        /// VRCAvatarDescriptor から Animator を取得します。
        /// </summary>
        public static bool TryGetAnimatorFromAvatar(GameObject avatarRoot, out Animator animator)
        {
            animator = null;

            if (avatarRoot == null)
            {
                return false;
            }

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return false;
            }

            animator = descriptor.GetComponent<Animator>();
            if (animator == null)
            {
                animator = descriptor.GetComponentInChildren<Animator>(true);
            }

            return animator != null;
        }

        /// <summary>
        /// basePrefabRoot（sourceChibiPrefab を展開したルート）から、
        /// VRCAvatarDescriptor の FX playable layer（Animator Controller）を取得します。
        ///
        /// - 取得できない場合でも例外は投げず、false を返します。
        /// - VRC SDK の型がロードされていない場合も false になります。
        /// </summary>
        public static bool TryGetFxPlayableLayerControllerFromBasePrefab(GameObject basePrefabRoot, out RuntimeAnimatorController fxController)
        {
            fxController = null;

            if (basePrefabRoot == null)
            {
                return false;
            }

            var descriptor = basePrefabRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? basePrefabRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return false;
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                fxController = layer.animatorController;
                return fxController != null;
            }

            return false;
        }

        /// <summary>
        /// basePrefabRoot（sourceChibiPrefab を展開したルート）から、
        /// VRCAvatarDescriptor の Expressions Menu / Parameters を取得します。
        ///
        /// - どちらか片方でも取得できれば true。
        /// - 取得できない場合でも例外は投げず、false を返します。
        /// </summary>
        public static bool TryGetExpressionsMenuAndParametersFromBasePrefab(
            GameObject basePrefabRoot,
            out ScriptableObject expressionsMenu,
            out ScriptableObject expressionParameters
        )
        {
            expressionsMenu = null;
            expressionParameters = null;

            if (basePrefabRoot == null)
            {
                return false;
            }

            var descriptor = basePrefabRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? basePrefabRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return false;
            }

            expressionsMenu = descriptor.expressionsMenu as ScriptableObject;
            expressionParameters = descriptor.expressionParameters as ScriptableObject;

            return expressionsMenu != null || expressionParameters != null;
        }

        /// <summary>
        /// dstRoot（Scene 上の複製先アバター）から、
        /// VRCAvatarDescriptor の FX Controller（PlayableLayer）を取得します。
        ///
        /// - 取得できない場合でも例外は投げず、false を返します。
        /// </summary>
        public static bool TryGetFxPlayableLayerControllerFromAvatar(GameObject dstRoot, out RuntimeAnimatorController fxController)
        {
            fxController = null;

            if (dstRoot == null)
            {
                return false;
            }

            var descriptor = dstRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? dstRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return false;
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                fxController = layer.animatorController;
                return fxController != null;
            }

            return false;
        }

        /// <summary>
        /// dstRoot（Scene 上の複製先アバター）から、
        /// VRCAvatarDescriptor の Expressions Menu / Parameters を取得します。
        ///
        /// - どちらか片方でも取得できれば true。
        /// - 取得できない場合でも例外は投げず、false を返します。
        /// </summary>
        public static bool TryGetExpressionsMenuAndParametersFromAvatar(
            GameObject dstRoot,
            out ScriptableObject expressionsMenu,
            out ScriptableObject expressionParameters
        )
        {
            expressionsMenu = null;
            expressionParameters = null;

            if (dstRoot == null)
            {
                return false;
            }

            var descriptor = dstRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? dstRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return false;
            }

            expressionsMenu = descriptor.expressionsMenu as ScriptableObject;
            expressionParameters = descriptor.expressionParameters as ScriptableObject;

            return expressionsMenu != null || expressionParameters != null;
        }

        /// <summary>
        /// VRCAvatarDescriptor の FX playable layer を差し替えます。
        /// </summary>
        public static void SetFxPlayableLayerController(GameObject dstRoot, RuntimeAnimatorController fxController)
        {
            if (dstRoot == null || fxController == null)
            {
                return;
            }

            var descriptor = dstRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? dstRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return;
            }

            Undo.RecordObject(descriptor, OCTLocalization.Get("Undo.SetFxPlayableLayer"));

            descriptor.customizeAnimationLayers = true;

            for (int i = 0; i < descriptor.baseAnimationLayers.Length; i++)
            {
                var layer = descriptor.baseAnimationLayers[i];
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                layer.isDefault = false;
                layer.isEnabled = true;
                layer.animatorController = fxController;
                descriptor.baseAnimationLayers[i] = layer;
                break;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
            EditorUtility.SetDirty(descriptor);
        }

        /// <summary>
        /// VRCAvatarDescriptor の expressionsMenu / expressionParameters を指定参照に変更します。
        /// </summary>
        public static void SetExpressionsMenuAndParameters(
            GameObject dstRoot,
            ScriptableObject expressionsMenu,
            ScriptableObject expressionParameters
        )
        {
            if (dstRoot == null)
            {
                return;
            }

            if (expressionsMenu == null && expressionParameters == null)
            {
                return;
            }

            var descriptor = dstRoot.GetComponent<VRCAvatarDescriptor>()
                             ?? dstRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                return;
            }

            VRCExpressionsMenu menu = null;
            if (expressionsMenu != null)
            {
                menu = expressionsMenu as VRCExpressionsMenu;
            }

            VRCExpressionParameters parameters = null;
            if (expressionParameters != null)
            {
                parameters = expressionParameters as VRCExpressionParameters;
            }

            if (menu == null && parameters == null)
            {
                return;
            }

            Undo.RecordObject(descriptor, OCTLocalization.Get("Undo.SetExpressionsReferences"));

            if (menu != null)
            {
                descriptor.expressionsMenu = menu;
            }

            if (parameters != null)
            {
                descriptor.expressionParameters = parameters;
            }
            descriptor.customExpressions = true;

            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
            EditorUtility.SetDirty(descriptor);
        }

        /// <summary>
        /// ViewPosition を sourcePrefab と同じにします。成功した場合 true。
        /// ※ログでは値（Vector3）を出さない想定です。
        /// </summary>
        public static bool TryCopyViewPositionFromBasePrefab(GameObject dstRoot, GameObject basePrefabRoot)
        {
            if (dstRoot == null || basePrefabRoot == null)
            {
                return false;
            }

            var dstDesc = dstRoot.GetComponent<VRCAvatarDescriptor>()
                          ?? dstRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            var srcDesc = basePrefabRoot.GetComponent<VRCAvatarDescriptor>()
                          ?? basePrefabRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);

            if (dstDesc == null || srcDesc == null)
            {
                return false;
            }

            var viewPos = srcDesc.ViewPosition;

            Undo.RecordObject(dstDesc, OCTLocalization.Get("Undo.CopyViewPosition"));
            dstDesc.ViewPosition = viewPos;

            PrefabUtility.RecordPrefabInstancePropertyModifications(dstDesc);
            EditorUtility.SetDirty(dstDesc);

            return true;
        }

        /// <summary>
        /// 複製したアバターの PipelineManager.blueprintId を空にします。
        /// </summary>
        public static bool ClearPipelineBlueprintId(GameObject avatarRoot, System.Collections.Generic.List<string> logs = null)
        {
            if (avatarRoot == null)
            {
                return false;
            }

            // 変更対象は「アバターのルートにある PipelineManager」のみに限定します。
            // 子階層まで辿ると、意図しない PipelineManager を消してしまう危険があります。
            var pipelineManager = avatarRoot.GetComponent<PipelineManager>();
            if (pipelineManager == null)
            {
                // ルートに無い場合は「複製対象として想定外」としてスキップします。
                // （必要なら別途、ルート構成を見直すのが安全です）
                logs?.Add(OCTLocalization.Get("Log.BlueprintClearSkippedMissing"));
                return false;
            }
            if (string.IsNullOrEmpty(pipelineManager.blueprintId))
            {
                logs?.Add(OCTLocalization.Get("Log.BlueprintClearSkippedEmpty"));
                return false;
            }

            Undo.RecordObject(pipelineManager, OCTLocalization.Get("Undo.ClearBlueprintId"));
            pipelineManager.blueprintId = string.Empty;

            PrefabUtility.RecordPrefabInstancePropertyModifications(pipelineManager);
            EditorUtility.SetDirty(pipelineManager);

            logs?.Add(OCTLocalization.Get("Log.BlueprintClearApplied"));
            return true;
        }
    }
}
#endif
