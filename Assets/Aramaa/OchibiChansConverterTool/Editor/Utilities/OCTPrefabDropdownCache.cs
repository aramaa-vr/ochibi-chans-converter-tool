#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTPrefabDropdownCache.cs
//
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
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// アバターの顔メッシュ情報を基に、候補となるおちびちゃんズ Prefab の一覧を作るキャッシュです。
    /// </summary>
    internal sealed class OCTPrefabDropdownCache
    {
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

        private static bool PrefabHasMatchingFaceMesh(string prefabPath, FaceMeshSignature targetFaceMeshSignature)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;
            if (!targetFaceMeshSignature.HasAnyIdentity) return false;

            return TryGetCachedFaceMeshSignature(prefabPath, out var prefabFaceMeshSignature) &&
                   FaceMeshSignatureMatches(targetFaceMeshSignature, prefabFaceMeshSignature);
        }

        /// <summary>
        /// アバターの Viseme 用メッシュから、GUID/LocalId を抽出します。
        /// </summary>
        private static bool TryGetFaceMeshSignature(GameObject root, out FaceMeshSignature signature)
        {
            signature = default;
            if (root == null) return false;

#if VRC_SDK_VRCSDK3
            var descriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (!TryGetVisemeRendererFromDescriptor(descriptor, out var faceRenderer)) return false;
            if (faceRenderer == null || faceRenderer.sharedMesh == null) return false;

            var mesh = faceRenderer.sharedMesh;
            if (!TryGetPrefabInfo(root, out var prefabGuid, out var prefabName))
            {
                prefabGuid = string.Empty;
                prefabName = string.Empty;
            }

            return TryBuildFaceMeshSignature(mesh, prefabGuid, prefabName, out signature);
#else
            return false;
#endif
        }

        private static bool TryGetFaceMeshSignatureFromPrefabPath(string prefabPath, out FaceMeshSignature signature)
        {
            signature = default;
            if (string.IsNullOrEmpty(prefabPath)) return false;

            var prefab = AssetDatabase.LoadMainAssetAtPath(prefabPath) as GameObject;
            if (prefab == null) return false;

            if (!TryGetFaceMeshSignature(prefab, out signature)) return false;

            var prefabSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab) ?? prefab;
            var sourcePath = AssetDatabase.GetAssetPath(prefabSource);
            if (string.IsNullOrEmpty(sourcePath))
            {
                sourcePath = prefabPath;
            }

            var prefabGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            var prefabName = sourcePath;
            signature = signature.WithPrefabInfo(prefabGuid, prefabName);
            return true;
        }

        /// <summary>
        /// Prefab の依存ハッシュを使って、顔メッシュIDのキャッシュを再利用します。
        /// </summary>
        private static bool TryGetCachedFaceMeshSignature(string prefabPath, out FaceMeshSignature signature)
        {
            signature = default;
            if (string.IsNullOrEmpty(prefabPath)) return false;

            EnsureFaceMeshCacheLoaded();

            var hash = AssetDatabase.GetAssetDependencyHash(prefabPath);
            if (CachedFaceMeshByPrefab.TryGetValue(prefabPath, out var cached) &&
                cached.DependencyHash == hash)
            {
                if (cached.HasFaceMesh)
                {
                    signature = cached.FaceMeshSignature;
                    return true;
                }

                return false;
            }

            var hasFaceMesh = TryGetFaceMeshSignatureFromPrefabPath(prefabPath, out var cachedSignature);
            CachedFaceMeshByPrefab[prefabPath] = new CachedFaceMesh(hash, cachedSignature, hasFaceMesh);
            MarkFaceMeshCacheDirty();
            // ここでは即時保存しません。
            // - OnDisable でまとめて保存される
            // - 検索中に毎回ディスク/設定を書き換える回数を減らし、Editor の負荷を抑える
            if (hasFaceMesh)
            {
                signature = cachedSignature;
            }

            return hasFaceMesh;
        }

        private static bool FaceMeshSignatureMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (MeshIdMatches(a.MeshId, b.MeshId))
            {
                return true;
            }

            if (PrefabGuidMatches(a, b)) return true;
            if (PrefabNameMatches(a, b)) return true;
            if (FbxGuidMatches(a, b)) return true;
            if (FbxNameMatches(a, b)) return true;
            if (AssetPathMatches(a, b)) return true;

            return false;
        }

        private static bool MeshIdMatches(MeshId a, MeshId b)
        {
            if (string.IsNullOrEmpty(a.Guid) || string.IsNullOrEmpty(b.Guid)) return false;
            if (!string.Equals(a.Guid, b.Guid, StringComparison.Ordinal)) return false;

            if (a.HasLocalId && b.HasLocalId)
            {
                return a.LocalId == b.LocalId;
            }

            return true;
        }

        private static bool PrefabGuidMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (string.IsNullOrEmpty(a.PrefabGuid) || string.IsNullOrEmpty(b.PrefabGuid)) return false;
            return string.Equals(a.PrefabGuid, b.PrefabGuid, StringComparison.Ordinal);
        }

        private static bool PrefabNameMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (string.IsNullOrEmpty(a.PrefabName) || string.IsNullOrEmpty(b.PrefabName)) return false;
            return string.Equals(a.PrefabName, b.PrefabName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool FbxGuidMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (string.IsNullOrEmpty(a.FbxGuid) || string.IsNullOrEmpty(b.FbxGuid)) return false;
            return string.Equals(a.FbxGuid, b.FbxGuid, StringComparison.Ordinal);
        }

        private static bool FbxNameMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (string.IsNullOrEmpty(a.FbxName) || string.IsNullOrEmpty(b.FbxName)) return false;
            return string.Equals(a.FbxName, b.FbxName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AssetPathMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (string.IsNullOrEmpty(a.FaceMeshAssetPath) || string.IsNullOrEmpty(b.FaceMeshAssetPath)) return false;
            return string.Equals(a.FaceMeshAssetPath, b.FaceMeshAssetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPrefabInfo(GameObject root, out string prefabGuid, out string prefabName)
        {
            prefabGuid = string.Empty;
            prefabName = string.Empty;
            if (root == null) return false;

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
            if (instanceRoot == null) return false;

            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            if (prefabAsset == null) return false;

            var sourceAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefabAsset) ?? prefabAsset;
            var prefabPath = AssetDatabase.GetAssetPath(sourceAsset);
            if (string.IsNullOrEmpty(prefabPath)) return false;

            prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            prefabName = prefabPath;
            return !string.IsNullOrEmpty(prefabGuid) || !string.IsNullOrEmpty(prefabName);
        }

        private static bool TryBuildFaceMeshSignature(
            Mesh mesh,
            string prefabGuid,
            string prefabName,
            out FaceMeshSignature signature)
        {
            signature = default;
            if (mesh == null) return false;

            var assetPath = AssetDatabase.GetAssetPath(mesh);
            var fbxGuid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
            var fbxName = string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath;
            var hasMeshId = TryBuildMeshId(mesh, out var meshId);

            if (!hasMeshId &&
                string.IsNullOrEmpty(prefabGuid) &&
                string.IsNullOrEmpty(prefabName) &&
                string.IsNullOrEmpty(fbxGuid) &&
                string.IsNullOrEmpty(fbxName) &&
                string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            signature = new FaceMeshSignature(meshId, prefabGuid, prefabName, fbxGuid, fbxName, assetPath);
            return true;
        }

        private static bool TryBuildMeshId(Mesh mesh, out MeshId meshId)
        {
            meshId = default;
            if (mesh == null) return false;

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out var guid, out long localId))
            {
                meshId = new MeshId(guid, localId, hasLocalId: true);
                return true;
            }

            var meshPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(meshPath)) return false;

            var fallbackGuid = AssetDatabase.AssetPathToGUID(meshPath);
            if (string.IsNullOrEmpty(fallbackGuid)) return false;

            meshId = new MeshId(fallbackGuid, 0, hasLocalId: false);
            return true;
        }

#if VRC_SDK_VRCSDK3
        private static bool TryGetVisemeRendererFromDescriptor(
            VRCAvatarDescriptor descriptor,
            out SkinnedMeshRenderer renderer)
        {
            renderer = null;
            if (descriptor == null) return false;

            using (var so = new SerializedObject(descriptor))
            {
                var prop = so.FindProperty("VisemeSkinnedMesh");
                if (prop != null)
                {
                    renderer = prop.objectReferenceValue as SkinnedMeshRenderer;
                }
            }

            if (renderer != null) return true;

            renderer = descriptor.VisemeSkinnedMesh;
            return renderer != null;
        }
#endif

        private static void EnsureFaceMeshCacheLoaded()
        {
            if (_faceMeshCacheLoaded) return;
            _faceMeshCacheLoaded = true;
            LoadFaceMeshCacheFromLibrary();
        }

        private static void MarkFaceMeshCacheDirty()
        {
            _faceMeshCacheDirty = true;
        }

        private static void LoadFaceMeshCacheFromLibrary()
        {
            try
            {
                var cachePath = GetFaceMeshCacheFilePath();
                if (!File.Exists(cachePath)) return;

                var json = File.ReadAllText(cachePath);
                if (string.IsNullOrEmpty(json)) return;

                var cacheFile = JsonUtility.FromJson<FaceMeshCacheFile>(json);
                if (cacheFile == null) return;
                if (cacheFile.Entries == null) return;

                foreach (var entry in cacheFile.Entries)
                {
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.PrefabPath)) continue;
                    if (string.IsNullOrEmpty(entry.DependencyHash)) continue;

                    if (!TryParseHash128(entry.DependencyHash, out var hash)) continue;

                    var meshId = new MeshId(entry.FaceMeshGuid ?? string.Empty, entry.FaceMeshLocalId, entry.HasLocalId);
                    var signature = new FaceMeshSignature(
                        meshId,
                        entry.PrefabGuid ?? string.Empty,
                        entry.PrefabName ?? string.Empty,
                        entry.FbxGuid ?? string.Empty,
                        entry.FbxName ?? string.Empty,
                        entry.FaceMeshAssetPath ?? string.Empty);
                    CachedFaceMeshByPrefab[entry.PrefabPath] = new CachedFaceMesh(hash, signature, entry.HasFaceMesh);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(OCTLocalization.Format("Warning.FaceMeshCacheLoadFailed", e.Message));
            }
        }

        private static void SaveFaceMeshCacheToLibrary()
        {
            if (!_faceMeshCacheDirty) return;

            try
            {
                var cacheFile = new FaceMeshCacheFile();
                foreach (var pair in CachedFaceMeshByPrefab)
                {
                    var cached = pair.Value;
                    cacheFile.Entries.Add(new FaceMeshCacheEntry
                    {
                        PrefabPath = pair.Key,
                        DependencyHash = cached.DependencyHash.ToString(),
                        FaceMeshGuid = cached.FaceMeshSignature.MeshId.Guid,
                        FaceMeshLocalId = cached.FaceMeshSignature.MeshId.LocalId,
                        HasLocalId = cached.FaceMeshSignature.MeshId.HasLocalId,
                        PrefabGuid = cached.FaceMeshSignature.PrefabGuid,
                        PrefabName = cached.FaceMeshSignature.PrefabName,
                        FbxGuid = cached.FaceMeshSignature.FbxGuid,
                        FbxName = cached.FaceMeshSignature.FbxName,
                        FaceMeshAssetPath = cached.FaceMeshSignature.FaceMeshAssetPath,
                        HasFaceMesh = cached.HasFaceMesh
                    });
                }

                var json = JsonUtility.ToJson(cacheFile, true);
                var cachePath = GetFaceMeshCacheFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? string.Empty);
                File.WriteAllText(cachePath, json);
                _faceMeshCacheDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning(OCTLocalization.Format("Warning.FaceMeshCacheSaveFailed", e.Message));
            }
        }

        private static string GetFaceMeshCacheFilePath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "Library", "Aramaa", "OchibiChansConverterTool", FaceMeshCacheFileName);
        }

        private static bool TryParseHash128(string value, out Hash128 hash)
        {
            hash = default;
            if (string.IsNullOrEmpty(value)) return false;

            try
            {
                hash = Hash128.Parse(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [Serializable]
        private sealed class FaceMeshCacheFile
        {
            public List<FaceMeshCacheEntry> Entries = new List<FaceMeshCacheEntry>();
        }

        /// <summary>
        /// フェイスメッシュキャッシュのシリアライズ用エントリです。
        /// </summary>
        [Serializable]
        private sealed class FaceMeshCacheEntry
        {
            public string PrefabPath;
            public string DependencyHash;
            public string FaceMeshGuid;
            public long FaceMeshLocalId;
            public bool HasLocalId;
            public bool HasFaceMesh;
            public string PrefabGuid;
            public string PrefabName;
            public string FbxGuid;
            public string FbxName;
            public string FaceMeshAssetPath;
        }

        /// <summary>
        /// メッシュ識別子（GUID と LocalId の組）です。
        /// </summary>
        private readonly struct MeshId
        {
            public MeshId(string guid, long localId, bool hasLocalId)
            {
                Guid = guid;
                LocalId = localId;
                HasLocalId = hasLocalId;
            }

            public string Guid { get; }
            public long LocalId { get; }
            public bool HasLocalId { get; }
        }

        /// <summary>
        /// 顔メッシュの識別情報（GUID/LocalId + Prefab/FBX 識別子）です。
        /// </summary>
        private readonly struct FaceMeshSignature
        {
            public FaceMeshSignature(
                MeshId meshId,
                string prefabGuid,
                string prefabName,
                string fbxGuid,
                string fbxName,
                string faceMeshAssetPath)
            {
                MeshId = meshId;
                PrefabGuid = prefabGuid;
                PrefabName = prefabName;
                FbxGuid = fbxGuid;
                FbxName = fbxName;
                FaceMeshAssetPath = faceMeshAssetPath;
            }

            public MeshId MeshId { get; }
            public string PrefabGuid { get; }
            public string PrefabName { get; }
            public string FbxGuid { get; }
            public string FbxName { get; }
            public string FaceMeshAssetPath { get; }

            public bool HasAnyIdentity =>
                !string.IsNullOrEmpty(MeshId.Guid) ||
                !string.IsNullOrEmpty(PrefabGuid) ||
                !string.IsNullOrEmpty(PrefabName) ||
                !string.IsNullOrEmpty(FbxGuid) ||
                !string.IsNullOrEmpty(FbxName) ||
                !string.IsNullOrEmpty(FaceMeshAssetPath);

            public FaceMeshSignature WithPrefabInfo(string prefabGuid, string prefabName)
            {
                return new FaceMeshSignature(MeshId, prefabGuid, prefabName, FbxGuid, FbxName, FaceMeshAssetPath);
            }
        }

        /// <summary>
        /// Prefab の依存ハッシュと顔メッシュIDをまとめたキャッシュ情報です。
        /// </summary>
        private readonly struct CachedFaceMesh
        {
            public CachedFaceMesh(Hash128 dependencyHash, FaceMeshSignature faceMeshSignature, bool hasFaceMesh)
            {
                DependencyHash = dependencyHash;
                FaceMeshSignature = faceMeshSignature;
                HasFaceMesh = hasFaceMesh;
            }

            public Hash128 DependencyHash { get; }
            public FaceMeshSignature FaceMeshSignature { get; }
            public bool HasFaceMesh { get; }
        }
    }
}
#endif
