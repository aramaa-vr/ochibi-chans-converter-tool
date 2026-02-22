#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - Hierarchy で選択したオブジェクトを「Ctrl+D と同じ方法」で複製します
// - 複製された “新しいオブジェクト” に対して、おちびちゃんズ用の同期処理を適用します
// - 変換元（おちびちゃんズ側）Prefab を指定し、VRCAvatarDescriptor から参照（FX/Menu/Parameters/ViewPosition 等）を抽出して反映します
// - sourceChibiPrefab（変換元Prefab）を 1 つ指定するだけで実行できます
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - このスクリプトは Editor 専用です（ビルドには含まれません）
// - Prefab を直接編集しないため、誤って元アセットを壊しにくいです
// - VRChat SDK の型は環境によって無い場合があるので、一部は反射 + SerializedObject で触ります
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
using UnityEngine.SceneManagement;

#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// 選択中アバターを Ctrl+D 相当で複製し、変換元 Prefab の設定を複製先に同期するエディターツールです。
    /// </summary>
    public static class OCTConversionApplier
    {
        private const string ToolVersion = OCTEditorConstants.ToolVersion;
        private const string LatestVersionUrl = OCTEditorConstants.LatestVersionUrl;
        private const string ToolWebsiteUrl = OCTEditorConstants.ToolWebsiteUrl;
        private const string ToolsMenuPath = OCTEditorConstants.ToolsMenuPath;
        private const string GameObjectMenuPath = OCTEditorConstants.GameObjectMenuPath;
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);
        private static string ToolWindowTitle => L("Tool.Name");
        private static string LogWindowTitle => L("Tool.LogWindowTitle");

        // ------------------------------------------------------------
        // MenuItem（入口）
        // ------------------------------------------------------------
        // 仕様：
        // - Tools/Aramaa からは「選択が無くても」ウィンドウを開ける
        // - GameObject/Aramaa からは「Hierarchy の単一選択（Scene上）」がある時だけ開ける

        /// <summary>
        /// Tools メニューから変換ウィンドウを開きます。
        /// </summary>
        [MenuItem(ToolsMenuPath, priority = 2000)]
        private static void OpenFromToolsMenu()
        {
            // Tools メニューは選択が無くても起動できる。
            // ただし、Scene上のオブジェクトが選択されている場合はそれをデフォルト対象にする。
            var selected = Selection.activeGameObject;
            if (selected != null && !EditorUtility.IsPersistent(selected) && selected.scene.IsValid() && selected.scene.isLoaded)
            {
                OCTConversionSourcePrefabWindow.Show(selected);
                return;
            }

            OCTConversionSourcePrefabWindow.Show(null);
        }

        /// <summary>
        /// Tools メニュー項目を有効化できるか判定します。
        /// </summary>
        [MenuItem(ToolsMenuPath, validate = true)]
        private static bool ValidateOpenFromToolsMenu()
        {
            // 開くだけなので常に有効。
            return true;
        }

        /// <summary>
        /// GameObject メニューから、現在選択中のオブジェクトを対象にウィンドウを開きます。
        /// </summary>
        [MenuItem(GameObjectMenuPath, priority = 0)]
        private static void OpenFromGameObjectMenu()
        {
            // GameObject メニューは “選択オブジェクト” を対象として起動する。
            var selected = Selection.activeGameObject;
            OCTConversionSourcePrefabWindow.Show(selected);
        }

        /// <summary>
        /// GameObject メニュー項目を表示可能か（Scene 上の単一選択があるか）を判定します。
        /// </summary>
        [MenuItem(GameObjectMenuPath, validate = true)]
        private static bool ValidateOpenFromGameObjectMenu()
        {
            // Hierarchy（Scene）上の単一選択があるときだけ表示
            var selected = Selection.activeGameObject;
            return selected != null && !EditorUtility.IsPersistent(selected) && selected.scene.IsValid() && selected.scene.isLoaded;
        }

        #region 変換元プレハブ選択ウィンドウ（EditorWindow）

        /// <summary>
        /// 変換元（おちびちゃんズ側）の Prefab アセットを参照欄で指定し、
        /// 選択中アバターを Ctrl+D 相当で複製した上で、複製先へ変換を適用するウィンドウです。
        ///
        /// 仕様（重要）
        /// - 指定された sourceChibiPrefab（おちびちゃんズ側の Prefab）に入っている
        ///   VRCAvatarDescriptor の内部設定（FX / Expressions / ViewPosition など）を読み取り、
        ///   その値を複製先へ反映します。
        /// - Ochibichans_Addmenu は sourceChibiPrefab の内部にある想定のため、
        ///   Prefab 内のネストされた Prefab インスタンスから該当アセットを探索して追加します。
        /// </summary>
        private sealed class OCTConversionSourcePrefabWindow : EditorWindow
        {
            private static string L(string key) => OCTLocalization.Get(key);
            private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

            // ------------------------------------------------------------
            // 見た目（ウィンドウサイズ）
            // ------------------------------------------------------------
            // 最低サイズのみ固定（内容が増える場合はスクロール対応）
            private static readonly Vector2 WindowMinSize = new Vector2(430, 620);

            // 二重起動防止：既に開いているウィンドウがあればそれを使う
            private static OCTConversionSourcePrefabWindow _opened;

            // 二重実行防止：ボタン連打で複数回の delayCall が積まれるのを防ぐ
            private bool _applyQueued;

            private bool _showLogs;
            private bool _applyMaboneProxyProcessing;
            private int _detectedMaboneProxyCount;
            private bool _isMaboneProxyCountDirty = true;
            private GameObject _maboneProxyCountSourceTarget;
            private Vector2 _scrollPosition;
            private bool _versionCheckRequested;
            private bool _versionCheckInProgress;
            private string _latestVersion;
            private string _versionError;
            private OCTVersionStatus _versionStatus = OCTVersionStatus.Unknown;

            private GUIStyle _descriptionStyle;
            private GUIStyle _cardStyle;
            private GUIStyle _sectionHeaderStyle;
            private GUIStyle _versionStatusStyle;
            private GUIStyle _accentButtonStyle;
            private GUIStyle _linkStyle;
            private bool _cachedProSkin;
            private bool _isWindowActive;

            // 変換対象（Hierarchy で選択されているアバター）
            private GameObject _sourceTarget;

            // 変換元（おちびちゃんズ側）Prefab アセット（Project 上の Prefab）
            private GameObject _sourcePrefabAsset;
            private readonly OCTPrefabDropdownCache _prefabDropdownCache = new OCTPrefabDropdownCache();

            /// <summary>
            /// 変換ウィンドウを表示します（既に開いていればフォーカスするだけ）。
            /// </summary>
            public static void Show(GameObject sourceTarget)
            {
                // sourceTarget は null でも良い（Tools メニューから起動できるようにする）

                // 既存ウィンドウがあるならそれを使う
                if (_opened != null)
                {
                    if (sourceTarget != null)
                    {
                        _opened._sourceTarget = sourceTarget;
                    }

                    _opened.Focus();
                    return;
                }

                // なければ作成
                var titleWithVersion = F("Window.TitleWithVersion", ToolWindowTitle, ToolVersion);
                var w = GetWindow<OCTConversionSourcePrefabWindow>(utility: true, title: titleWithVersion, focus: true);
                _opened = w;

                w.minSize = WindowMinSize;
                if (sourceTarget != null)
                {
                    w._sourceTarget = sourceTarget;
                }

                // ユーザーが「何をするウィンドウか」を一目で理解できるタイトル
                w.titleContent = new GUIContent(titleWithVersion);

                w.Show();
                w.Focus();
            }

            private void OnDisable()
            {
                _isWindowActive = false;

                // 閉じられたら参照を解放（次回また開けるように）
                if (_opened == this)
                {
                    _opened = null;
                }

                UnregisterMaboneProxyDirtyCallbacks();
                OCTPrefabDropdownCache.SaveCacheToDisk();
            }

            private void OnEnable()
            {
                _isWindowActive = true;
                _versionCheckRequested = false;
                _versionCheckInProgress = false;
                _latestVersion = null;
                _versionError = null;
                _versionStatus = OCTVersionStatus.Unknown;
                _cachedProSkin = EditorGUIUtility.isProSkin;
                _isMaboneProxyCountDirty = true;
                _maboneProxyCountSourceTarget = null;
                RegisterMaboneProxyDirtyCallbacks();
                ClearCachedStyles();
            }

            /// <summary>
            /// MA BoneProxy 検出キャッシュの再計算トリガーを登録します。
            ///
            /// 目的: OnGUI のたびに重い検索をしないため、
            /// Hierarchy 変更や Undo/Redo が起きた時だけ dirty フラグを立てます。
            /// </summary>
            private void RegisterMaboneProxyDirtyCallbacks()
            {
                // 多重登録防止
                EditorApplication.hierarchyChanged -= MarkMaboneProxyCountDirty;
                Undo.undoRedoPerformed -= MarkMaboneProxyCountDirty;

                EditorApplication.hierarchyChanged += MarkMaboneProxyCountDirty;
                Undo.undoRedoPerformed += MarkMaboneProxyCountDirty;
            }

            /// <summary>
            /// RegisterMaboneProxyDirtyCallbacks で登録したイベントを解除します。
            /// ウィンドウ破棄時のリークや重複発火を防ぐため必ず対で呼びます。
            /// </summary>
            private void UnregisterMaboneProxyDirtyCallbacks()
            {
                EditorApplication.hierarchyChanged -= MarkMaboneProxyCountDirty;
                Undo.undoRedoPerformed -= MarkMaboneProxyCountDirty;
            }

            private void OnGUI()
            {
                UpdateWindowTitle();
                InitializeStyles();

                // ------------------------------------------------------------
                // このウィンドウは「元のアバター」と「おちびちゃんズ側 Prefab アセット」を指定し、
                // 「実行」ボタンで変換を行うツールです。
                //
                // 注意:
                // - OnGUI（IMGUI）中に Hierarchy を編集すると Layout が崩れやすいので、
                //   実際の処理（複製→変換）は delayCall で次の Editor ループに回します。
                // ------------------------------------------------------------

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                try
                {
                    EditorGUILayout.Space(8);

                    DrawCard(() =>
                    {
                        DrawLanguageSelector();
                        EnsureVersionCheck();
                        DrawVersionStatus();
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField(L("Window.Description"), _descriptionStyle ?? EditorStyles.wordWrappedLabel);
                    });

                    EditorGUILayout.Space(6);

                    DrawCard(() =>
                    {
                        DrawSectionHeader("1", L("Section.SourceAvatarLabel"));
                        DrawTargetObjectField();
                    });

                    EditorGUILayout.Space(6);

                    DrawCard(() =>
                    {
                        DrawSectionHeader("2", L("Section.TargetPrefabLabel"));
                        DrawSourcePrefabObjectField();
                    });

                    EditorGUILayout.Space(6);

                    DrawCard(() =>
                    {
                        DrawSectionHeader("3", L("Button.Execute"));
                        DrawExecuteButton();
                        EditorGUILayout.Space(4);
                        DrawMaboneProxyToggle();
                    });

                    EditorGUILayout.Space(6);

                    DrawCard(OpenToolWebsite);

                    EditorGUILayout.Space(6);

                    DrawCard(DrawLogToggle);

                    EditorGUILayout.Space(10);
                }
                catch (ExitGUIException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
            }

            private void ClearCachedStyles()
            {
                _descriptionStyle = null;
                _cardStyle = null;
                _sectionHeaderStyle = null;
                _versionStatusStyle = null;
                _accentButtonStyle = null;
                _linkStyle = null;
            }

            private void InitializeStyles(bool forceRebuild = false)
            {
                if (!forceRebuild && _descriptionStyle != null && _cachedProSkin == EditorGUIUtility.isProSkin)
                {
                    return;
                }

                _cachedProSkin = EditorGUIUtility.isProSkin;

                _descriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    richText = true
                };

                _cardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10),
                    margin = new RectOffset(4, 4, 2, 2)
                };

                _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    wordWrap = true
                };

                _versionStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };

                var buttonStyle = GUI.skin != null && GUI.skin.button != null ? GUI.skin.button : EditorStyles.miniButton;
                _accentButtonStyle = new GUIStyle(buttonStyle)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 36
                };

                _linkStyle = new GUIStyle(EditorStyles.linkLabel)
                {
                    wordWrap = true
                };
            }

            private void DrawCard(Action drawContent)
            {
                using (new EditorGUILayout.VerticalScope(_cardStyle ?? EditorStyles.helpBox))
                {
                    drawContent?.Invoke();
                }
            }

            private void DrawSectionHeader(string step, string title)
            {
                EditorGUILayout.LabelField($"{step}. {title}", _sectionHeaderStyle ?? EditorStyles.boldLabel);
                EditorGUILayout.Space(2);
            }

            private void DrawVersionStatus()
            {
                var message = GetVersionStatusMessage(out var color);
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                var messageType = GetVersionStatusMessageType();
                if (messageType != MessageType.None)
                {
                    EditorGUILayout.HelpBox(message, messageType);
                    return;
                }

                ApplyStatusColor(_versionStatusStyle, color);
                EditorGUILayout.LabelField(message, _versionStatusStyle ?? EditorStyles.miniLabel);
            }

            private void UpdateWindowTitle()
            {
                var titleWithVersion = F("Window.TitleWithVersion", ToolWindowTitle, ToolVersion);
                if (titleContent == null || titleContent.text != titleWithVersion)
                {
                    titleContent = new GUIContent(titleWithVersion);
                }
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
                        Repaint();
                    }
                }
            }

            private void EnsureVersionCheck()
            {
                if (_versionCheckRequested)
                {
                    return;
                }

                _versionCheckRequested = true;

                if (string.IsNullOrWhiteSpace(LatestVersionUrl))
                {
                    _versionError = L("Version.ErrorMissingUrl");
                    _versionStatus = OCTVersionStatus.Unknown;
                    return;
                }

                _versionCheckInProgress = true;
                _versionError = null;
                _latestVersion = null;
                _versionStatus = OCTVersionStatus.Unknown;

                OCTVersionUtility.FetchLatestVersionAsync(LatestVersionUrl, result =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (!_isWindowActive)
                        {
                            return;
                        }

                        _versionCheckInProgress = false;

                        if (!result.Succeeded)
                        {
                            _versionError = string.IsNullOrWhiteSpace(result.Error) ? L("Version.Unknown") : result.Error;
                            _versionStatus = OCTVersionStatus.Unknown;
                            Repaint();
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(result.LatestVersion))
                        {
                            _versionError = L("Version.ExtractFailed");
                            _versionStatus = OCTVersionStatus.Unknown;
                            Repaint();
                            return;
                        }

                        _latestVersion = result.LatestVersion;
                        _versionStatus = OCTVersionUtility.GetVersionStatus(ToolVersion, _latestVersion);
                        Repaint();
                    };
                });
            }

            private string GetVersionStatusMessage(out Color color)
            {
                if (_versionCheckInProgress)
                {
                    color = SelectStatusColor(new Color(0.2f, 0.6f, 1f), new Color(0.1f, 0.3f, 0.8f));
                    return F("Version.Checking", ToolVersion);
                }

                if (!string.IsNullOrWhiteSpace(_versionError))
                {
                    color = SelectStatusColor(new Color(0.95f, 0.35f, 0.35f), new Color(0.7f, 0.15f, 0.15f));
                    return F("Version.CheckFailed", ToolVersion, _versionError);
                }

                if (string.IsNullOrWhiteSpace(_latestVersion))
                {
                    color = SelectStatusColor(new Color(0.7f, 0.7f, 0.7f), new Color(0.45f, 0.45f, 0.45f));
                    return F("Version.NoInfo", ToolVersion);
                }

                switch (_versionStatus)
                {
                    case OCTVersionStatus.UpdateAvailable:
                        color = SelectStatusColor(new Color(1f, 0.65f, 0.2f), new Color(0.8f, 0.45f, 0.1f));
                        return F("Version.Available", ToolVersion, _latestVersion);
                    case OCTVersionStatus.Ahead:
                        color = SelectStatusColor(new Color(0.4f, 0.75f, 1f), new Color(0.15f, 0.5f, 0.8f));
                        return F("Version.Ahead", ToolVersion, _latestVersion);
                    case OCTVersionStatus.UpToDate:
                        color = SelectStatusColor(new Color(0.35f, 0.8f, 0.4f), new Color(0.15f, 0.55f, 0.2f));
                        return F("Version.UpToDate", ToolVersion);
                    default:
                        color = SelectStatusColor(new Color(0.7f, 0.7f, 0.7f), new Color(0.45f, 0.45f, 0.45f));
                        return F("Version.Unknown", ToolVersion);
                }
            }

            private MessageType GetVersionStatusMessageType()
            {
                if (_versionCheckInProgress)
                {
                    return MessageType.Info;
                }

                if (!string.IsNullOrWhiteSpace(_versionError))
                {
                    return MessageType.Warning;
                }

                switch (_versionStatus)
                {
                    case OCTVersionStatus.UpdateAvailable:
                        return MessageType.Warning;
                    case OCTVersionStatus.UpToDate:
                    case OCTVersionStatus.Ahead:
                        return MessageType.None;
                    default:
                        return MessageType.None;
                }
            }

            private static Color SelectStatusColor(Color proSkinColor, Color lightSkinColor)
            {
                return EditorGUIUtility.isProSkin ? proSkinColor : lightSkinColor;
            }

            private static void ApplyStatusColor(GUIStyle style, Color color)
            {
                if (style == null)
                {
                    return;
                }

                style.normal.textColor = color;
                style.hover.textColor = color;
                style.active.textColor = color;
                style.focused.textColor = color;
            }

            /// <summary>
            /// 変換対象（元のアバター）の参照欄を描画します。
            /// </summary>
            private void DrawTargetObjectField()
            {
                EditorGUI.BeginChangeCheck();
                var nextTarget = (GameObject)EditorGUILayout.ObjectField(_sourceTarget, typeof(GameObject), allowSceneObjects: true);
                if (EditorGUI.EndChangeCheck())
                {
                    _sourceTarget = nextTarget;
                    RefreshSourceTargetDependentState(clearSourcePrefabAsset: true);
                }

                if (_sourceTarget == null)
                {
                    EditorGUILayout.HelpBox(L("Help.SelectSourceAvatar"), MessageType.Warning);
                    return;
                }

                // Project 上のアセットを入れてしまった場合は対象外（実行条件を明確化）
                if (EditorUtility.IsPersistent(_sourceTarget))
                {
                    EditorGUILayout.HelpBox(L("Help.SourceAvatarAssetInvalid"), MessageType.Error);
                    _sourceTarget = null;
                    RefreshSourceTargetDependentState(clearSourcePrefabAsset: false);
                }
            }

            /// <summary>
            /// sourceTarget 依存の状態（Prefab 候補キャッシュ、MA BoneProxy 検出キャッシュ）を更新します。
            /// </summary>
            private void RefreshSourceTargetDependentState(bool clearSourcePrefabAsset)
            {
                _prefabDropdownCache.MarkNeedsRefresh();
                if (clearSourcePrefabAsset)
                {
                    _sourcePrefabAsset = null;
                }

                ResetMaboneProxyDetectionCache();
            }

            /// <summary>
            /// MA BoneProxy 検出キャッシュを初期化し、次回描画時に再計算させます。
            /// </summary>
            private void ResetMaboneProxyDetectionCache()
            {
                _maboneProxyCountSourceTarget = null;
                _detectedMaboneProxyCount = 0;
                MarkMaboneProxyCountDirty();
            }

            /// <summary>
            /// 変換元（おちびちゃんズ側 Prefab アセット）の参照欄を描画します。
            /// </summary>
            private void DrawSourcePrefabObjectField()
            {
                _prefabDropdownCache.RefreshIfNeeded(_sourceTarget);

                var hasCandidates = _prefabDropdownCache.CandidateDisplayNames.Count > 0;

                if (!hasCandidates)
                {
                    EditorGUILayout.HelpBox(L("Help.NoPrefabCandidates"), MessageType.Info);

                    EditorGUILayout.LabelField(L("Section.ManualPrefabLabel"), EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var manualPrefab = (GameObject)EditorGUILayout.ObjectField(_sourcePrefabAsset, typeof(GameObject), allowSceneObjects: false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _sourcePrefabAsset = manualPrefab;
                    }

                    if (_sourcePrefabAsset == null)
                    {
                        EditorGUILayout.HelpBox(L("Help.SelectPrefabFromProject"), MessageType.Info);
                        return;
                    }

                    if (!IsPrefabAsset(_sourcePrefabAsset))
                    {
                        EditorGUILayout.HelpBox(L("Help.NotPrefabSelected"), MessageType.Error);
                        return;
                    }

                    EditorGUILayout.HelpBox(L("Help.ManualPrefabWarning"), MessageType.Warning);
                    return;
                }

                var candidateDisplayNames = _prefabDropdownCache.CandidateDisplayNames?.ToArray() ?? Array.Empty<string>();
                if (candidateDisplayNames.Length == 0)
                {
                    EditorGUILayout.HelpBox(L("Help.SelectPrefabFromProject"), MessageType.Info);
                    return;
                }

                var currentIndex = Mathf.Clamp(_prefabDropdownCache.SelectedIndex, 0, candidateDisplayNames.Length - 1);
                var nextIndex = EditorGUILayout.Popup(L("Label.CandidateList"), currentIndex, candidateDisplayNames);
                if (nextIndex != currentIndex)
                {
                    _prefabDropdownCache.ApplySelection(nextIndex);
                }

                _sourcePrefabAsset = _prefabDropdownCache.SourcePrefabAsset;
                using (new EditorGUI.DisabledScope(true))
                {
                    _sourcePrefabAsset = (GameObject)EditorGUILayout.ObjectField(_sourcePrefabAsset, typeof(GameObject), allowSceneObjects: false);
                }

                if (_sourcePrefabAsset == null)
                {
                    EditorGUILayout.HelpBox(L("Help.SelectPrefabFromProject"), MessageType.Info);
                    return;
                }

                if (!IsPrefabAsset(_sourcePrefabAsset))
                {
                    EditorGUILayout.HelpBox(L("Help.NotPrefabSelected"), MessageType.Error);
                }
            }

            /// <summary>
            /// 実行ボタンを描画し、押されたら安全に delayCall へ処理を逃がします。
            /// </summary>
            private void DrawExecuteButton()
            {
                var canExecute =
                    !_applyQueued &&
                    _sourceTarget != null &&
                    !EditorUtility.IsPersistent(_sourceTarget) &&
                    _sourceTarget.scene.IsValid() &&
                    _sourceTarget.scene.isLoaded &&
                    _sourcePrefabAsset != null &&
                    IsPrefabAsset(_sourcePrefabAsset);

                using (new EditorGUI.DisabledScope(!canExecute))
                {
                    var executeLabel = new GUIContent(
                        L("Button.Execute"),
                        EditorGUIUtility.IconContent("d_PlayButton").image);

                    if (GUILayout.Button(executeLabel, _accentButtonStyle ?? EditorStyles.miniButton, GUILayout.Height(38)))
                    {
                        QueueApplyFromFields();
                    }
                }

                if (_applyQueued)
                {
                    EditorGUILayout.HelpBox(L("Help.ExecuteQueued"), MessageType.Info);
                }
            }

            private void DrawLogToggle()
            {
                _showLogs = EditorGUILayout.ToggleLeft(L("Toggle.ShowLogs"), _showLogs);
            }

            private void OpenToolWebsite()
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 左側のアイコン（Unity 標準の情報アイコン）
                    var icon = EditorGUIUtility.IconContent("console.infoicon");
                    GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

                    using (new EditorGUILayout.VerticalScope())
                    {
                        var hasValidUrl = Uri.TryCreate(ToolWebsiteUrl, UriKind.Absolute, out var parsedUrl)
                            && (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);

                        using (new EditorGUI.DisabledScope(!hasValidUrl))
                        {
                            if (GUILayout.Button(L("Button.DiscordHelp"), _linkStyle ?? EditorStyles.linkLabel))
                            {
                                Application.OpenURL(ToolWebsiteUrl);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// MA BoneProxy 補正トグルと補足メッセージを描画します。
            ///
            /// - 検出件数はキャッシュ経由で取得（毎描画で全探索しない）
            /// - 検出あり + OFF のときのみ警告表示
            /// - ON のときは補正内容の説明を表示
            /// </summary>
            private void DrawMaboneProxyToggle()
            {
                EnsureDetectedMaboneProxyCount();

                _applyMaboneProxyProcessing = EditorGUILayout.ToggleLeft(L("Toggle.MaboneProxy"), _applyMaboneProxyProcessing);

                if (_detectedMaboneProxyCount > 0 && !_applyMaboneProxyProcessing)
                {
                    EditorGUILayout.HelpBox(F("Help.MaboneProxyRecommendOrAdjustWhenDetected", _detectedMaboneProxyCount), MessageType.Warning);
                }

                if (_applyMaboneProxyProcessing)
                {
                    EditorGUILayout.HelpBox(L("Help.MaboneProxy"), MessageType.Info);
                }
            }

            /// <summary>
            /// MA BoneProxy 検出数キャッシュを必要時のみ更新します。
            /// dirty でなく、かつ対象アバターが同じ場合は再計算しません。
            /// </summary>
            private void EnsureDetectedMaboneProxyCount()
            {
                if (!_isMaboneProxyCountDirty && _maboneProxyCountSourceTarget == _sourceTarget)
                {
                    return;
                }

                _detectedMaboneProxyCount = CountDetectedMaboneProxies(_sourceTarget);
                _maboneProxyCountSourceTarget = _sourceTarget;
                _isMaboneProxyCountDirty = false;
            }

            /// <summary>
            /// MA BoneProxy 検出キャッシュを無効化します（次回描画時に再計算）。
            /// </summary>
            private void MarkMaboneProxyCountDirty()
            {
                _isMaboneProxyCountDirty = true;
            }

            /// <summary>
            /// 対象アバター配下の ModularAvatarBoneProxy コンポーネント数を数えます。
            /// MA 未導入環境では 0 を返して安全にスキップします。
            /// </summary>
            private static int CountDetectedMaboneProxies(GameObject avatarRoot)
            {
                if (avatarRoot == null || !OCTModularAvatarUtility.IsModularAvatarAvailable)
                {
                    return 0;
                }

#if CHIBI_MODULAR_AVATAR
                var proxies = avatarRoot.GetComponentsInChildren<ModularAvatarBoneProxy>(true);
                return proxies?.Length ?? 0;
#else
                return 0;
#endif
            }

            /// <summary>
            /// 入力欄の内容で「複製→変換」を予約します。
            /// </summary>
            private void QueueApplyFromFields()
            {
                // 二重実行防止
                if (_applyQueued)
                {
                    return;
                }

                if (_sourceTarget == null || EditorUtility.IsPersistent(_sourceTarget) || !_sourceTarget.scene.IsValid() || !_sourceTarget.scene.isLoaded)
                {
                    EditorUtility.DisplayDialog(
                        L("Dialog.ToolTitle"),
                        L("Dialog.SelectSourceAvatar"),
                        L("Dialog.Ok")
                    );
                    return;
                }

                if (_sourcePrefabAsset == null || !IsPrefabAsset(_sourcePrefabAsset))
                {
                    EditorUtility.DisplayDialog(
                        L("Dialog.ToolTitle"),
                        L("Dialog.SelectSourcePrefab"),
                        L("Dialog.Ok")
                    );
                    return;
                }

                _applyQueued = true;

                var capturedSourcePrefab = _sourcePrefabAsset;
                var capturedTarget = _sourceTarget;
                var capturedApplyMaboneProxyProcessing = _applyMaboneProxyProcessing;

                var capturedTargetName = capturedTarget != null ? capturedTarget.name : L("Log.NullValue");
                Debug.Log(F("Log.QueuedApply", capturedTargetName, capturedSourcePrefab.name));

                // SVG 対応ステップ: 入口（UI）
                EditorApplication.delayCall += () =>
                {
                    var logs = new List<string>();

                    try
                    {
                        var applySucceeded = OCTConversionPipeline.DuplicateThenApply(
                            capturedSourcePrefab,
                            capturedTarget,
                            capturedApplyMaboneProxyProcessing,
                            logs
                        );

                        if (applySucceeded && capturedTarget != null && capturedTarget.activeSelf)
                        {
                            // SVG 対応ステップ: 8) 完了
                            Undo.RecordObject(capturedTarget, L("Undo.DuplicateApply"));
                            capturedTarget.SetActive(false);
                            EditorUtility.SetDirty(capturedTarget);
                        }
                    }
                    catch (Exception e)
                    {
                        logs.Add(L("Log.Error.ExceptionOccurred"));
                        Debug.LogException(e);
                    }
                    finally
                    {
                        // 次回も使えるようにフラグを戻す
                        _applyQueued = false;

                        if (_showLogs)
                        {
                            // ログウィンドウを表示（メインウィンドウ内には表示しない）
                            OCTConversionLogWindow.ShowLogs(LogWindowTitle, logs);
                        }

                        if (_isWindowActive && _opened == this)
                        {
                            Repaint();
                        }
                    }
                };

                // 以降の IMGUI 処理を打ち切り（レイアウト崩壊回避）
                GUIUtility.ExitGUI();
            }

            /// <summary>
            /// 「Project 上の Prefab アセット」かどうかを判定します。
            /// </summary>
            private static bool IsPrefabAsset(GameObject go)
            {
                if (go == null)
                {
                    return false;
                }

                if (!EditorUtility.IsPersistent(go))
                {
                    // Scene上オブジェクトを除外
                    return false;
                }

                var path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                return PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;
            }
        }

        #endregion

    }
}
#endif
