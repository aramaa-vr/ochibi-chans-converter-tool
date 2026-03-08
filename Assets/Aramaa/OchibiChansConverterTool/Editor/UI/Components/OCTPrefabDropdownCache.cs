#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - アバターの顔メッシュに一致するおちびちゃんズ Prefab を高速に検索するキャッシュです
// - Prefab の依存ハッシュと紐づけて、再走査の回数を減らします
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - VRChat SDK が無い環境では検索できないため、安全にスキップします
// - Project 側の Prefab を読み取るだけで、Prefab 自体の内容は変更しません
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - キャッシュは Library 配下に保存し、VCS には含めません（各環境ローカル）
// - Prefab 判定ロジックを変える際は、候補優先順位の理由もコメントに残す
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// アバターの顔メッシュ情報を基に、候補となるおちびちゃんズ Prefab の一覧を作るキャッシュです。
    /// </summary>
    internal sealed partial class OCTPrefabDropdownCache
    {
        // --------------------------------------------------------------------
        // partial ファイル構成（初見の人向け）
        // --------------------------------------------------------------------
        // - OCTPrefabDropdownCache.cs
        //   候補一覧の状態管理・公開API・基本フロー。
        // - OCTPrefabDropdownCache.FaceMeshMatching.cs
        //   FaceMeshSignature の抽出/一致判定（比較ロジック本体）。
        // - OCTPrefabDropdownCache.Persistence.cs
        //   Library 配下キャッシュの読み書き（JSON）。
        // - OCTPrefabDropdownCache.Models.cs
        //   判定と保存で共有する値オブジェクト定義。
        // --------------------------------------------------------------------
        // 呼び出しフロー（概要）:
        // RefreshIfNeeded
        //   -> PrefabHasMatchingFaceMesh
        //     -> TryGetCachedFaceMeshSignature
        //       -> (cache miss時) TryGetFaceMeshSignatureFromPrefabPath
        //          -> TryGetFaceMeshSignature / TryBuildFaceMeshSignature
        // OnDisable時の SaveCacheToDisk -> SaveFaceMeshCacheToLibrary
        // ※ 上記の実体は partial 先ファイルに分割されています。
        private static string L(string key) => OCTLocalization.Get(key);
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        private const string BaseFolder = OCTEditorConstants.BaseFolder;

        // Library に保存するファイル名（プロジェクト単位・ユーザー単位）。
        // 末尾の v7 は「キャッシュ互換性（このキャッシュを再利用して良いか）」のバージョン。
        // 互換が壊れる変更を入れたら v7 に上げる（JSON構造が同じでも上げてよい）。
        private const string FaceMeshCacheFileName = "FaceMeshCache.v7.json";

        private static readonly Dictionary<string, CachedFaceMesh> CachedFaceMeshByPrefab =
            new Dictionary<string, CachedFaceMesh>();
        private static bool _faceMeshCacheLoaded;
        private static bool _faceMeshCacheDirty;

        private int _cachedTargetInstanceId;
        private bool _needsRefreshPrefabs = true;
        private int _selectedPrefabIndex;
        private GameObject _sourcePrefabAsset;
        private readonly List<string> _candidatePrefabPaths = new List<string>();
        private readonly List<string> _candidateDisplayNames = new List<string>();

        public IReadOnlyList<string> CandidateDisplayNames => _candidateDisplayNames;
        public int SelectedIndex => _selectedPrefabIndex;
        public GameObject SourcePrefabAsset => _sourcePrefabAsset;

        /// <summary>
        /// 指定した Prefab が候補一覧に含まれるかを判定します。
        /// </summary>
        public bool ContainsCandidate(GameObject prefab)
        {
            if (prefab == null || _candidatePrefabPaths.Count == 0)
            {
                return false;
            }

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return false;
            }

            return _candidatePrefabPaths.Any(path =>
                string.Equals(path, prefabPath, StringComparison.Ordinal));
        }

        public static void SaveCacheToDisk()
        {
            SaveFaceMeshCacheToLibrary();
        }

        /// <summary>
        /// 対象アバターの変更に備えて、次回の候補再構築を予約します。
        /// </summary>
        public void MarkNeedsRefresh()
        {
            _needsRefreshPrefabs = true;
        }

        /// <summary>
        /// ドロップダウンで選ばれたインデックスを反映し、選択中 Prefab を更新します。
        /// </summary>
        public void ApplySelection(int nextIndex)
        {
            if (_candidatePrefabPaths.Count == 0)
            {
                _sourcePrefabAsset = null;
                return;
            }

            _selectedPrefabIndex = Mathf.Clamp(nextIndex, 0, _candidatePrefabPaths.Count - 1);
            var selectedPath = _candidatePrefabPaths[_selectedPrefabIndex];
            _sourcePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(selectedPath);
        }

        /// <summary>
        /// 手動入力された Prefab を反映します。
        /// 候補一覧に同一 Prefab がある場合は選択状態も同期します。
        /// </summary>
        public void ApplyManualSelection(GameObject manualPrefab)
        {
            if (manualPrefab == null)
            {
                _sourcePrefabAsset = null;
                return;
            }

            var manualPath = AssetDatabase.GetAssetPath(manualPrefab);
            if (string.IsNullOrEmpty(manualPath))
            {
                _sourcePrefabAsset = manualPrefab;
                return;
            }

            // 同一パスを再ロードして参照を正規化しておく（SubAsset混在時の揺れ防止）
            _sourcePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(manualPath) ?? manualPrefab;

            if (_candidatePrefabPaths.Count == 0)
            {
                return;
            }

            var matchedIndex = _candidatePrefabPaths.FindIndex(path =>
                string.Equals(path, manualPath, StringComparison.Ordinal));
            if (matchedIndex >= 0)
            {
                _selectedPrefabIndex = matchedIndex;
            }
        }

        /// <summary>
        /// 対象アバターが変わったときのみ候補を再計算します。
        /// </summary>
        public void RefreshIfNeeded(GameObject sourceTarget)
        {
            if (sourceTarget == null)
            {
                ClearState();
                return;
            }

            var instanceId = sourceTarget.GetInstanceID();
            if (!_needsRefreshPrefabs && instanceId == _cachedTargetInstanceId) return;

            _needsRefreshPrefabs = false;
            _cachedTargetInstanceId = instanceId;

            _candidatePrefabPaths.Clear();
            _candidateDisplayNames.Clear();
            _selectedPrefabIndex = 0;
            _sourcePrefabAsset = null;

            if (!TryGetFaceMeshSignature(sourceTarget, out var avatarFaceMeshSignature)) return;

            var subFolders = AssetDatabase.GetSubFolders(BaseFolder);
            foreach (var folder in subFolders)
            {
                var prefabPath = FindPreferredPrefabPathUnder(folder);
                if (string.IsNullOrEmpty(prefabPath)) continue;

                if (!PrefabHasMatchingFaceMesh(prefabPath, avatarFaceMeshSignature)) continue;

                _candidatePrefabPaths.Add(prefabPath);
                _candidateDisplayNames.Add(Path.GetFileName(folder));
            }

            if (_candidatePrefabPaths.Count > 0)
            {
                ApplySelection(_selectedPrefabIndex);
            }
        }

        private void ClearState()
        {
            _candidatePrefabPaths.Clear();
            _candidateDisplayNames.Clear();
            _sourcePrefabAsset = null;
            _cachedTargetInstanceId = 0;
            _selectedPrefabIndex = 0;
            _needsRefreshPrefabs = false;
        }

        /// <summary>
        /// 指定フォルダ配下の Prefab から、優先順位に従って候補を1つ選びます。
        /// </summary>
        private static string FindPreferredPrefabPathUnder(string folder)
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            if (prefabGuids == null || prefabGuids.Length == 0) return null;

            var candidates = new List<string>();
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                candidates.Add(path);
            }

            if (candidates.Count == 0) return null;

            var preferred = PickPrefabByFilenamePattern(candidates, "Kisekae Variant");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            preferred = PickPrefabByFilenamePattern(candidates, "Kaihen_Kisekae");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            preferred = PickPrefabByFilenamePattern(candidates, "Kisekae");
            if (!string.IsNullOrEmpty(preferred)) return preferred;

            return candidates[0];
        }

        private static string PickPrefabByFilenamePattern(IEnumerable<string> paths, string pattern)
        {
            if (paths == null) return null;
            if (string.IsNullOrEmpty(pattern)) return null;

            var match = paths.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path)
                    .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);

            return match;
        }

    }
}
#endif
