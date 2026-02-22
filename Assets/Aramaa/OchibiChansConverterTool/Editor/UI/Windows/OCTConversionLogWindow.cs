#if UNITY_EDITOR
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
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 変換ログを表示・コピーするための専用ウィンドウです。
    /// </summary>
    internal sealed class OCTConversionLogWindow : EditorWindow
    {
        private sealed class LogSection
        {
            public string Title;
            public readonly List<string> Lines = new List<string>();
            public bool IsExpanded = true;
            public Vector2 ContentScroll;
        }

        private static string L(string key) => OCTLocalization.Get(key);

        private static OCTConversionLogWindow _opened;

        private readonly List<string> _logs = new List<string>();
        private readonly List<LogSection> _sections = new List<LogSection>();
        private string _cachedText = string.Empty;
        private Vector2 _scroll;
        private string _targetPrefix;
        private string _stepHeaderPrefix;

        private static readonly Vector2 DefaultMinSize = new Vector2(720, 500);
        private static readonly GUIStyle ReadOnlyLogStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = false,
            richText = false,
            padding = new RectOffset(8, 8, 6, 6)
        };

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
                _opened.ShowUtility();
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

            _cachedText = _logs.Count > 0 ? string.Join("\n", _logs) : L("LogWindow.NoLogs");
            _targetPrefix = BuildPrefixFromFormat(L("Log.TargetEntry"));
            _stepHeaderPrefix = BuildPrefixFromFormat(L("Log.Step.Header"));
            RebuildSections();
            _scroll = Vector2.zero;
            Repaint();
        }

        private void RebuildSections()
        {
            _sections.Clear();

            if (_logs.Count == 0)
            {
                return;
            }

            string currentTargetLabel = null;
            LogSection current = CreateSection("Overview", true);

            foreach (var rawLine in _logs)
            {
                var line = rawLine ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (current.Lines.Count > 0 && !string.IsNullOrEmpty(current.Lines[current.Lines.Count - 1]))
                    {
                        current.Lines.Add(string.Empty);
                    }

                    continue;
                }

                if (IsSeparatorLine(line))
                {
                    continue;
                }

                if (IsTargetLine(line))
                {
                    currentTargetLabel = line;
                    current = CreateSection(currentTargetLabel, true);
                    continue;
                }

                if (IsStepHeaderLine(line))
                {
                    var title = string.IsNullOrEmpty(currentTargetLabel)
                        ? line
                        : $"{currentTargetLabel} / {line}";
                    current = CreateSection(title, true);
                    continue;
                }

                current.Lines.Add(line);
            }

            _sections.RemoveAll(x => x == null || x.Lines.Count == 0);
            if (_sections.Count == 0)
            {
                var noLogsSection = CreateSection("Logs", true);
                noLogsSection.Lines.Add(L("LogWindow.NoLogs"));
            }
        }

        private LogSection CreateSection(string title, bool expanded)
        {
            var section = new LogSection
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Logs" : title,
                IsExpanded = expanded
            };
            _sections.Add(section);
            return section;
        }

        private static bool IsStepHeaderLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_opened?._stepHeaderPrefix) && line.StartsWith(_opened._stepHeaderPrefix, StringComparison.Ordinal))
            {
                return true;
            }

            return line.StartsWith("[Step ", StringComparison.Ordinal);
        }

        private static bool IsTargetLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_opened?._targetPrefix) && line.StartsWith(_opened._targetPrefix, StringComparison.Ordinal))
            {
                return true;
            }

            return line.StartsWith("対象:", StringComparison.Ordinal)
                || line.StartsWith("Target:", StringComparison.Ordinal);
        }

        private static string BuildPrefixFromFormat(string format)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            var placeholderIndex = format.IndexOf("{0}", StringComparison.Ordinal);
            if (placeholderIndex >= 0)
            {
                return format.Substring(0, placeholderIndex);
            }

            return format;
        }

        private static bool IsSeparatorLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            foreach (var c in line)
            {
                if (c != '-')
                {
                    return false;
                }
            }

            return line.Length >= 10;
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("LogWindow.CopyButton"), GUILayout.Height(24)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _cachedText ?? string.Empty;
                    }

                    if (GUILayout.Button("すべて展開", GUILayout.Width(90), GUILayout.Height(24)))
                    {
                        SetAllExpanded(true);
                    }

                    if (GUILayout.Button("すべて折りたたむ", GUILayout.Width(110), GUILayout.Height(24)))
                    {
                        SetAllExpanded(false);
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(L("LogWindow.CloseButton"), GUILayout.Width(80), GUILayout.Height(24)))
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.Space(6);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                for (int i = 0; i < _sections.Count; i++)
                {
                    DrawSection(_sections[i], i + 1);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void SetAllExpanded(bool expanded)
        {
            foreach (var section in _sections)
            {
                section.IsExpanded = expanded;
            }

            Repaint();
        }

        private void DrawSection(LogSection section, int index)
        {
            if (section == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                section.IsExpanded = EditorGUILayout.Foldout(
                    section.IsExpanded,
                    $"{index}. {section.Title}",
                    true,
                    EditorStyles.foldout);

                if (!section.IsExpanded)
                {
                    return;
                }

                var text = JoinLines(section.Lines);
                var lineCount = Mathf.Max(1, section.Lines.Count);
                var contentHeight = Mathf.Max(120f, lineCount * 18f + 12f);
                var viewportHeight = Mathf.Clamp(contentHeight, 120f, 240f);

                section.ContentScroll = EditorGUILayout.BeginScrollView(section.ContentScroll, GUILayout.Height(viewportHeight));
                EditorGUILayout.SelectableLabel(text, ReadOnlyLogStyle, GUILayout.MinHeight(contentHeight));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(4);
        }

        private static string JoinLines(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(lines[i]);
            }

            return sb.ToString();
        }
    }
}
#endif
