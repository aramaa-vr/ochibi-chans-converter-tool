#if UNITY_EDITOR
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
        private const string WindowTitle = "衣装スケール調整ツール";
        private const string MenuPath = "Aramaa/対応衣装スケール調整ツール (Outfit Scale Adjuster)";
        private const string HelpVideoUrl = "https://youtu.be/Zh0Z0pzjmdk";

        private readonly List<string> _modificationLog = new List<string>();

        [MenuItem(MenuPath)]
        private static void ShowWindow()
        {
            var window = GetWindow<OCTCostumeScaleModifierWindow>(WindowTitle);

            // 既存仕様に合わせて DPI スケール込みの固定サイズにします。
            var dpiScale = EditorGUIUtility.pixelsPerPoint;
            var fixedSize = new Vector2(350, 200) * dpiScale;
            window.minSize = fixedSize;
            window.maxSize = fixedSize;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("スケール変更したアバターへ対応衣装を合わせるツールです。", EditorStyles.boldLabel);

            HandleDragAndDrop();

            EditorGUILayout.Space();
            if (GUILayout.Button("説明用動画を見る", EditorStyles.linkLabel))
            {
                Application.OpenURL(HelpVideoUrl);
            }

            DrawCustomHelpBox(
                "1. スケール変更したアバターをHierarchy直下に置く\n" +
                "2. 衣装オブジェクトを右クリックし、[ModularAvatar] Setup Outfitを選択\n" +
                "3. 対応衣装をHierarchy直下に置く\n" +
                "4. 対応衣装をスケール変更したアバターにドラッグ＆ドロップ\n" +
                "5. 対応衣装をこのツールにドラッグ＆ドロップ"
            );

            DrawCenteredLabel();
        }

        private void HandleDragAndDrop()
        {
            if (Event.current.type != EventType.DragUpdated && Event.current.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (Event.current.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            var costumes = new List<GameObject>();
            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject is GameObject gameObject && !costumes.Contains(gameObject))
                {
                    costumes.Add(gameObject);
                }
            }

            if (costumes.Count == 0)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            foreach (var costume in costumes)
            {
                Undo.RegisterCompleteObjectUndo(costume, "Modify Scale");
            }

            _modificationLog.Clear();
            var totalAppliedCount = 0;
            foreach (var costume in costumes)
            {
                if (costume == null)
                {
                    continue;
                }

                _modificationLog.Add($"スケール調整対象: {costume.name}");
                ApplyCostumeScaleByParentDescriptor(costume.transform);
                var appliedCount = OCTModularAvatarCostumeScaleAdjuster.AdjustByMergeArmatureMapping(costume, _modificationLog);
                totalAppliedCount += appliedCount;
                _modificationLog.Add($"適用数: {appliedCount}");
                _modificationLog.Add(string.Empty);
            }

            if (totalAppliedCount == 0)
            {
                _modificationLog.Add("スケール調整は 0 件でした。Modular Avatar 未導入、または Merge Armature マッピングが無い可能性があります。");
            }

            OCTConversionLogWindow.ShowLogs("衣装スケール調整ログ", _modificationLog);

            Undo.CollapseUndoOperations(undoGroup);

            Event.current.Use();
        }

        private static void ApplyCostumeScaleByParentDescriptor(Transform costumeTransform)
        {
            if (costumeTransform == null)
            {
                return;
            }

            var descriptorOwner = FindParentDescriptorOwner(costumeTransform.parent);
            if (descriptorOwner == null)
            {
                return;
            }

            costumeTransform.localScale = Vector3.Scale(costumeTransform.localScale, descriptorOwner.localScale);
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
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.LabelField("対応衣装をここに", centerStyle);
            EditorGUILayout.LabelField("ドラッグ＆ドロップ", centerStyle);
        }
    }
}
#endif
