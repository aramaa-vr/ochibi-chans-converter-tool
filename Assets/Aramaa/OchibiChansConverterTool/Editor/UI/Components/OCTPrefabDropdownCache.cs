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
        // BaseFolder 配下のうち、候補検索から外すサブフォルダ名。
        private const string ExcludedSearchSubFolderName = "缶バッジ";
        private const string ExcludedSearchFolder = BaseFolder + "/" + ExcludedSearchSubFolderName;
        private static readonly string ExcludedSearchFolderPrefix = ExcludedSearchFolder + "/";

        // Library に保存するファイル名（プロジェクト単位・ユーザー単位）。
        // 末尾の v10 は「キャッシュ互換性（このキャッシュを再利用して良いか）」のバージョン。
        // 互換が壊れる変更を入れたら v10 以降へ更新する（JSON構造が同じでも上げてよい）。
        private const string FaceMeshCacheFileName = "FaceMeshCache.v10.json";

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

            return _candidatePrefabPaths.Exists(path =>
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

            // 候補生成フロー（高コスト箇所をまとめて最適化）:
            // 1) BaseFolder直下のサブフォルダ一覧を取得
            // 2) BaseFolder全体を1回だけ走査して「各サブフォルダの優先Prefab」を集約
            // 3) その結果を使って FaceMesh 一致判定を行い、最終候補へ追加
            var subFolders = AssetDatabase.GetSubFolders(BaseFolder);
            if (subFolders == null || subFolders.Length == 0) return;

            // サブフォルダ数を上限目安にして容量を確保。
            // List の再確保を減らし、候補追加時の GC 発生を抑える。
            EnsureCandidateListCapacity(subFolders.Length);

            var preferredPrefabStateByFolder = BuildPreferredPrefabStateBySubFolder(subFolders);
            foreach (var folder in subFolders)
            {
                if (!preferredPrefabStateByFolder.TryGetValue(folder, out var state)) continue;

                var prefabPath = state.GetPreferredPath();
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
        /// ドロップダウン候補リストの内部容量を事前確保します。
        /// 候補追加時の再割り当てを減らし、GC 負荷を抑えるための補助メソッドです。
        /// </summary>
        private void EnsureCandidateListCapacity(int expectedCount)
        {
            if (expectedCount <= 0) return;

            if (_candidatePrefabPaths.Capacity < expectedCount)
            {
                _candidatePrefabPaths.Capacity = expectedCount;
            }

            if (_candidateDisplayNames.Capacity < expectedCount)
            {
                _candidateDisplayNames.Capacity = expectedCount;
            }
        }

        /// <summary>
        /// BaseFolder 配下の Prefab を1回だけ走査し、
        /// 「サブフォルダごとの優先Prefab候補状態」を構築します。
        ///
        /// 以前のようにサブフォルダごとに FindAssets を呼ばず、
        /// 走査回数を抑えて候補生成の負荷を下げることが目的です。
        /// </summary>
        private static Dictionary<string, PreferredPrefabState> BuildPreferredPrefabStateBySubFolder(string[] subFolders)
        {
            if (subFolders == null || subFolders.Length == 0)
            {
                return new Dictionary<string, PreferredPrefabState>(StringComparer.Ordinal);
            }

            var statesByFolder = new Dictionary<string, PreferredPrefabState>(subFolders.Length, StringComparer.Ordinal);
            foreach (var folder in subFolders)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                if (IsPathExcludedFromSearch(folder)) continue;
                statesByFolder[folder] = default;
            }

            if (statesByFolder.Count == 0)
            {
                return statesByFolder;
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { BaseFolder });
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                return statesByFolder;
            }

            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsPathExcludedFromSearch(path)) continue;

                if (!TryGetImmediateSubFolder(path, out var folder)) continue;
                if (!statesByFolder.TryGetValue(folder, out var state)) continue;

                state.RegisterCandidate(path);
                statesByFolder[folder] = state;
            }

            return statesByFolder;
        }

        private static bool IsPathExcludedFromSearch(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            return string.Equals(assetPath, ExcludedSearchFolder, StringComparison.Ordinal) ||
                   assetPath.StartsWith(ExcludedSearchFolderPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Asset パスから BaseFolder 直下のサブフォルダパスを取り出します。
        /// 例: Assets/.../Base/CharA/Model.prefab -> Assets/.../Base/CharA
        /// </summary>
        private static bool TryGetImmediateSubFolder(string assetPath, out string subFolderPath)
        {
            subFolderPath = null;
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (string.IsNullOrEmpty(BaseFolder)) return false;

            var prefix = BaseFolder + "/";
            if (!assetPath.StartsWith(prefix, StringComparison.Ordinal)) return false;

            var relative = assetPath.Substring(prefix.Length);
            var slashIndex = relative.IndexOf('/');
            if (slashIndex <= 0) return false;

            var subFolderName = relative.Substring(0, slashIndex);
            subFolderPath = prefix + subFolderName;
            return true;
        }

        /// <summary>
        /// サブフォルダ内での「優先Prefab選定状態」を保持します。
        /// - first: どの優先条件にも当てはまらない場合のフォールバック
        /// - best: 優先順位に基づく現在の最有力候補
        /// </summary>
        private struct PreferredPrefabState
        {
            private string _firstCandidate;
            private string _bestCandidate;
            private int _bestPriority;

            public void RegisterCandidate(string path)
            {
                if (string.IsNullOrEmpty(path)) return;

                // 最初に見つかった Prefab は常にフォールバックとして保持する。

                if (string.IsNullOrEmpty(_firstCandidate))
                {
                    _firstCandidate = path;
                    _bestPriority = int.MaxValue;
                }

                // 優先度0（Kisekae Variant）は最良値なので、以降の比較は不要。
                if (_bestPriority == 0) return;

                var fileName = Path.GetFileNameWithoutExtension(path);
                var currentPriority = GetPrefabNamePriority(fileName);
                if (currentPriority >= _bestPriority) return;

                _bestPriority = currentPriority;
                _bestCandidate = path;
            }

            public string GetPreferredPath()
            {
                return _bestCandidate ?? _firstCandidate;
            }
        }

        /// <summary>
        /// ファイル名ベースの優先度を返します（値が小さいほど優先）。
        /// 既存仕様:
        ///   0: "Kisekae Variant"
        ///   1: "Kaihen_Kisekae"
        ///   2: "Kisekae"
        ///   3: その他
        /// </summary>
        private static int GetPrefabNamePriority(string fileNameWithoutExtension)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExtension)) return 3;

            if (fileNameWithoutExtension.IndexOf("Kisekae Variant", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            if (fileNameWithoutExtension.IndexOf("Kaihen_Kisekae", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            if (fileNameWithoutExtension.IndexOf("Kisekae", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            return 3;
        }

    }
}
#endif
