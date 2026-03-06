#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

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

        private const string MenuPath = "Tools/Aramaa/対応衣装スケール調整ツール (Outfit Scale Adjuster)";
        private const string HelpVideoUrl = "https://youtu.be/Zh0Z0pzjmdk";

        private readonly List<string> _modificationLog = new List<string>();

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

        private void HandleDragAndDrop()
        {
            if (Event.current.type != EventType.DragUpdated && Event.current.type != EventType.DragPerform)
            {
                return;
            }

            var costumes = CollectValidSceneCostumes();
            if (costumes.Count == 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (Event.current.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.AcceptDrag();

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            foreach (var costume in costumes)
            {
                Undo.RegisterCompleteObjectUndo(costume, "Modify Scale");
            }

            _modificationLog.Clear();
            var hasAnyModification = false;
            foreach (var costume in costumes)
            {
                if (costume == null)
                {
                    continue;
                }

                _modificationLog.Add(F("CostumeScaleWindow.LogTarget", costume.name));
                var rootScaleChanged = ApplyCostumeScaleByParentDescriptor(costume.transform);
                var appliedCount = OCTModularAvatarCostumeScaleAdjuster.AdjustByMergeArmatureMapping(costume, _modificationLog, out var copiedScaleAdjusterCount);
                hasAnyModification |= rootScaleChanged || appliedCount > 0 || copiedScaleAdjusterCount > 0;
                _modificationLog.Add(F("CostumeScaleWindow.LogAppliedCount", appliedCount));
                _modificationLog.Add(string.Empty);
            }

            if (!hasAnyModification)
            {
                _modificationLog.Add(L("CostumeScaleWindow.LogNoApplied"));
            }

            OCTConversionLogWindow.ShowLogs(L("CostumeScaleWindow.LogTitle"), _modificationLog);

            Undo.CollapseUndoOperations(undoGroup);

            Event.current.Use();
        }

        private static List<GameObject> CollectValidSceneCostumes()
        {
            var costumes = new List<GameObject>();
            var addedCostumes = new HashSet<GameObject>();
            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (!(draggedObject is GameObject gameObject) || !IsValidSceneCostume(gameObject))
                {
                    continue;
                }

                if (!addedCostumes.Add(gameObject))
                {
                    continue;
                }

                costumes.Add(gameObject);
            }

            return costumes;
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

        private static bool ApplyCostumeScaleByParentDescriptor(Transform costumeTransform)
        {
            if (costumeTransform == null)
            {
                return false;
            }

            var descriptorOwner = FindParentDescriptorOwner(costumeTransform.parent);
            if (descriptorOwner == null)
            {
                return false;
            }

            return OCTModularAvatarCostumeScaleAdjuster.TryApplyScaleModifier(costumeTransform, descriptorOwner.localScale);
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
            EditorGUILayout.Space();
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
