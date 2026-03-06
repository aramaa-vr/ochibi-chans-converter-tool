#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 「衣装スケール調整ツール」の UI。
    /// 既存の見た目・文言を維持し、処理本体は共通ロジックへ委譲します。
    /// </summary>
    internal sealed class OCTCostumeScaleModifierWindow : EditorWindow
    {
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        private const string MenuPath = "Aramaa/対応衣装スケール調整ツール (Outfit Scale Adjuster)";
        private const string HelpVideoUrl = "https://youtu.be/Zh0Z0pzjmdk";
        private readonly List<string> _modificationLog = new List<string>();
        private bool _showLogWindow = false;

        [MenuItem(MenuPath)]
        private static void ShowWindow()
        {
            var window = GetWindow<OCTCostumeScaleModifierWindow>(L("CostumeScaleWindow.Title"));
            window.UpdateWindowTitle();

            // 既存仕様に合わせて DPI スケール込みの固定サイズにします。
            var dpiScale = EditorGUIUtility.pixelsPerPoint;
            var fixedSize = new Vector2(450, 250) * dpiScale;
            window.minSize = fixedSize;
            window.maxSize = fixedSize;
        }


        private void OnEnable()
        {
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            titleContent = new GUIContent(L("CostumeScaleWindow.Title"));
        }

        private void OnGUI()
        {
            DrawLanguageSelector();
            EditorGUILayout.LabelField(L("CostumeScaleWindow.Description"), EditorStyles.boldLabel);

            HandleDragAndDrop();

            EditorGUILayout.Space();
            if (GUILayout.Button(L("CostumeScaleWindow.VideoLink"), EditorStyles.linkLabel))
            {
                Application.OpenURL(HelpVideoUrl);
            }

            DrawCustomHelpBox(L("CostumeScaleWindow.HelpSteps"));

            DrawCenteredLabel();
            GUILayout.FlexibleSpace();
            DrawShowLogToggle();
        }

        private void DrawLanguageSelector()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(L("Language.Label"));

                var displayNames = OCTLocalization.GetLanguageDisplayNames() ?? Array.Empty<string>();
                if (displayNames.Length == 0)
                {
                    return;
                }

                var currentIndex = Mathf.Clamp(OCTLocalization.GetLanguageIndex(), 0, displayNames.Length - 1);
                var nextIndex = EditorGUILayout.Popup(currentIndex, displayNames);
                if (nextIndex != currentIndex)
                {
                    OCTLocalization.SetLanguage(OCTLocalization.GetLanguageCodeFromIndex(nextIndex));
                    UpdateWindowTitle();
                    Repaint();
                }
            }
        }

        /// <summary>
        /// Drag&Drop の入口。
        /// イベント種別ごとに責務を分けるため、switch で分岐します。
        /// </summary>
        private void HandleDragAndDrop()
        {
            switch (Event.current.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    return;
                case EventType.DragPerform:
                    HandleDragPerform();
                    return;
                default:
                    return;
            }
        }

        /// <summary>
        /// ドロップ確定時の処理。
        /// 受理 → 前提チェック → バリデーション → 適用 の順で進めます。
        /// </summary>
        private void HandleDragPerform()
        {
            DragAndDrop.AcceptDrag();

            if (DragAndDrop.objectReferences.Length == 0)
            {
                Event.current.Use();
                return;
            }

            if (!OCTModularAvatarUtility.IsModularAvatarAvailable)
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ToolTitle"),
                    L("CostumeScaleWindow.ModularAvatarMissingDialog"),
                    L("Dialog.Ok")
                );
                Event.current.Use();
                return;
            }

            var validation = CollectValidSceneCostumes();
            var costumes = validation.ValidCostumes;
            if (costumes.Count == 0)
            {
                if (validation.HasInvalidOutfitCandidate)
                {
                    ShowInvalidOutfitDialog();
                }

                Event.current.Use();
                return;
            }

            if (validation.HasInvalidOutfitCandidate)
            {
                ShowInvalidOutfitDialog();
            }

            ApplyScaleAdjustments(costumes);
            Event.current.Use();
        }

        /// <summary>
        /// 有効な衣装群に対してスケール調整を適用し、必要に応じてログ/警告を表示します。
        /// </summary>
        private void ApplyScaleAdjustments(List<GameObject> costumes)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            foreach (var costume in costumes)
            {
                Undo.RegisterCompleteObjectUndo(costume, "Modify Scale");
            }

            _modificationLog.Clear();
            var hasAnyModification = false;
            var costumesWithoutDescriptor = new List<string>();
            foreach (var costume in costumes)
            {
                if (costume == null)
                {
                    continue;
                }

                _modificationLog.Add(F("CostumeScaleWindow.LogTarget", costume.name));
                var rootScaleChanged = ApplyCostumeScaleByParentDescriptor(costume.transform, out var hasParentDescriptor);
                if (!hasParentDescriptor)
                {
                    costumesWithoutDescriptor.Add(costume.name);
                    _modificationLog.Add(L("CostumeScaleWindow.LogDescriptorNotFound"));
                }

                var appliedCount = OCTModularAvatarCostumeScaleAdjuster.AdjustByMergeArmatureMapping(costume, _modificationLog, out var copiedScaleAdjusterCount);
                hasAnyModification |= rootScaleChanged || appliedCount > 0 || copiedScaleAdjusterCount > 0;
                _modificationLog.Add(F("CostumeScaleWindow.LogAppliedCount", appliedCount));
                _modificationLog.Add(string.Empty);
            }

            if (!hasAnyModification)
            {
                _modificationLog.Add(L("CostumeScaleWindow.LogNoApplied"));
            }

            if (_showLogWindow)
            {
                OCTConversionLogWindow.ShowLogs(L("CostumeScaleWindow.LogTitle"), _modificationLog);
            }

            if (costumesWithoutDescriptor.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    L("Dialog.ToolTitle"),
                    F("CostumeScaleWindow.MissingDescriptorDialog", string.Join(", ", costumesWithoutDescriptor)),
                    L("Dialog.Ok")
                );
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private static CostumeDragValidationResult CollectValidSceneCostumes()
        {
            var costumes = new List<GameObject>();
            var addedCostumes = new HashSet<GameObject>();
            var hasInvalidOutfitCandidate = false;
            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (!TryGetDraggedGameObject(draggedObject, out var gameObject))
                {
                    hasInvalidOutfitCandidate = true;
                    continue;
                }

                if (!IsValidSceneCostume(gameObject))
                {
                    hasInvalidOutfitCandidate = true;
                    continue;
                }

                if (!IsCompatibleOutfit(gameObject))
                {
                    hasInvalidOutfitCandidate = true;
                    continue;
                }

                if (!addedCostumes.Add(gameObject))
                {
                    continue;
                }

                costumes.Add(gameObject);
            }

            return new CostumeDragValidationResult(costumes, hasInvalidOutfitCandidate);
        }

        private static bool TryGetDraggedGameObject(UnityEngine.Object draggedObject, out GameObject gameObject)
        {
            switch (draggedObject)
            {
                case GameObject go:
                    gameObject = go;
                    return true;
                case Component component:
                    gameObject = component.gameObject;
                    return gameObject != null;
                default:
                    gameObject = null;
                    return false;
            }
        }

        private static bool IsCompatibleOutfit(GameObject gameObject)
        {
#if CHIBI_MODULAR_AVATAR
            return gameObject.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
#else
            return false;
#endif
        }

        private static bool IsValidSceneCostume(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            if (EditorUtility.IsPersistent(gameObject) || PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                return false;
            }

            return gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        private static bool ApplyCostumeScaleByParentDescriptor(Transform costumeTransform, out bool hasParentDescriptor)
        {
            hasParentDescriptor = false;
            if (costumeTransform == null)
            {
                return false;
            }

            var descriptorOwner = FindParentDescriptorOwner(costumeTransform.parent);
            if (descriptorOwner == null)
            {
                return false;
            }

            hasParentDescriptor = true;

            return OCTModularAvatarCostumeScaleAdjuster.TryApplyScaleModifier(costumeTransform, descriptorOwner.localScale);
        }

        private readonly struct CostumeDragValidationResult
        {
            internal CostumeDragValidationResult(List<GameObject> validCostumes, bool hasInvalidOutfitCandidate)
            {
                ValidCostumes = validCostumes;
                HasInvalidOutfitCandidate = hasInvalidOutfitCandidate;
            }

            internal List<GameObject> ValidCostumes { get; }
            internal bool HasInvalidOutfitCandidate { get; }
        }

        private static Transform FindParentDescriptorOwner(Transform from)
        {
            var current = from;
            while (current != null)
            {
                if (current.GetComponent<VRCAvatarDescriptor>() != null)
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }


        private static void ShowInvalidOutfitDialog()
        {
            EditorUtility.DisplayDialog(
                L("Dialog.ToolTitle"),
                L("CostumeScaleWindow.InvalidOutfitDialog"),
                L("Dialog.Ok")
            );
        }

        /// <summary>
        /// 処理ログウィンドウを表示するかどうかの切り替え。
        /// 設定は EditorPrefs に保存し、次回起動時も引き継ぎます。
        /// </summary>
        private void DrawShowLogToggle()
        {
            var nextValue = EditorGUILayout.ToggleLeft(L("CostumeScaleWindow.ShowLogToggle"), _showLogWindow);
            if (nextValue == _showLogWindow)
            {
                return;
            }

            _showLogWindow = nextValue;
        }

        private static void DrawCustomHelpBox(string message)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.95f, 0.95f);

            GUILayout.BeginVertical("box");
            {
                GUI.backgroundColor = originalColor;
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon"), GUILayout.Width(24), GUILayout.Height(24));
                    EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private static void DrawCenteredLabel()
        {
            EditorGUILayout.Space();

            var centerStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.LabelField(L("CostumeScaleWindow.DropHereLine1"), centerStyle);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L("CostumeScaleWindow.DropHereLine2"), centerStyle);
        }
    }
}
#endif
