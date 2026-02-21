#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Windows/OCTConversionLogWindow.cs
//
// ============================================================================
// 概要
// ============================================================================
// - OchibiChansConverterTool の変換処理で「何をどう変更したか」を詳細に表示するログ専用ウィンドウです。
// - メインウィンドウ（変換UI）にはログを表示せず、別ウィンドウに集約します。
// - コピーボタンで全文をクリップボードへ送れます。
//
// 注意:
// - ここで表示するログは “値（数値）” を極力出さず、「名前」「パス」「参照アセット」を中心にします。
//   （スケール値 / BlendShapeウェイト値は表示しません）
// ============================================================================

using System;
using System.Collections.Generic;
using Aramaa.OchibiChansConverterTool.Editor.Utilities;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 変換ログを表示・コピーするための専用ウィンドウです。
    /// </summary>
    internal sealed class OCTConversionLogWindow : EditorWindow
    {
        private static OCTConversionLogWindow _opened;

        private readonly List<string> _logs = new List<string>();
        private string _cachedText = string.Empty;
        private Vector2 _scroll;

        private static readonly Vector2 DefaultMinSize = new Vector2(640, 440);

        /// <summary>
        /// ログウィンドウを表示し、内容を差し替えます（既に開いていれば再利用）。
        /// </summary>
        public static void ShowLogs(string windowTitle, List<string> logs)
        {
            if (_opened == null)
            {
                _opened = CreateInstance<OCTConversionLogWindow>();
                _opened.minSize = DefaultMinSize;
                _opened.titleContent = new GUIContent(windowTitle);
                _opened.ShowUtility(); // “補助ウィンドウ”として表示
            }
            else
            {
                _opened.titleContent = new GUIContent(windowTitle);
                _opened.Focus();
            }

            _opened.SetLogs(logs);
        }

        private void OnDisable()
        {
            if (_opened == this)
            {
                _opened = null;
            }
        }

        private void SetLogs(List<string> logs)
        {
            _logs.Clear();
            if (logs != null && logs.Count > 0)
            {
                _logs.AddRange(logs);
            }

            _cachedText = _logs.Count > 0 ? string.Join("\n", _logs) : OCTLocalizationService.Get("LogWindow.NoLogs");
            _scroll = Vector2.zero;
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(OCTLocalizationService.Get("LogWindow.CopyButton"), GUILayout.Height(24)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _cachedText ?? string.Empty;
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(OCTLocalizationService.Get("LogWindow.CloseButton"), GUILayout.Width(80), GUILayout.Height(24)))
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.Space(6);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                // TextArea で “全文” を表示すると、コピーしたい時に選択もしやすい
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(_cachedText ?? string.Empty, GUILayout.ExpandHeight(true));
                }

                EditorGUILayout.EndScrollView();
            }
        }
    }
}
#endif
