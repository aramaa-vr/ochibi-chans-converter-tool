#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - FaceMesh キャッシュの読み書き（Library 配下）を担当する分割ファイルです。
// - キャッシュ形式の DTO（FaceMeshCacheFile / Entry）もここに集約しています。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - 保存先は Library 配下のため、通常 VCS には含まれません。
// - 依存ハッシュでキャッシュを無効化するため、Prefab 更新時は自動で再計算されます。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal sealed partial class OCTPrefabDropdownCache
    {
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
                    var avatarId = new MeshId(entry.AvatarGuid ?? string.Empty, entry.AvatarLocalId, entry.AvatarHasLocalId);
                    var signature = new FaceMeshSignature(
                        meshId,
                        avatarId,
                        entry.AvatarAssetPath ?? string.Empty,
                        entry.PrefabGuid ?? string.Empty,
                        entry.PrefabName ?? string.Empty,
                        entry.OriginalAvatarPrefabPath ?? string.Empty,
                        entry.MergeAnimatorDiffJson ?? string.Empty,
                        entry.FbxGuid ?? string.Empty,
                        entry.FbxName ?? string.Empty,
                        entry.FaceMeshAssetPath ?? string.Empty);
                    CachedFaceMeshByPrefab[entry.PrefabPath] = new CachedFaceMesh(hash, signature, entry.HasFaceMesh);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("Warning.FaceMeshCacheLoadFailed", e.Message));
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
                        AvatarGuid = cached.FaceMeshSignature.AvatarId.Guid,
                        AvatarLocalId = cached.FaceMeshSignature.AvatarId.LocalId,
                        AvatarHasLocalId = cached.FaceMeshSignature.AvatarId.HasLocalId,
                        AvatarAssetPath = cached.FaceMeshSignature.AvatarAssetPath,
                        PrefabGuid = cached.FaceMeshSignature.PrefabGuid,
                        PrefabName = cached.FaceMeshSignature.PrefabName,
                        OriginalAvatarPrefabPath = cached.FaceMeshSignature.OriginalAvatarPrefabPath,
                        MergeAnimatorDiffJson = cached.FaceMeshSignature.MergeAnimatorDiffJson,
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
                Debug.LogWarning(F("Warning.FaceMeshCacheSaveFailed", e.Message));
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
            public string AvatarGuid;
            public long AvatarLocalId;
            public bool AvatarHasLocalId;
            public string AvatarAssetPath;
            public string PrefabGuid;
            public string PrefabName;
            public string OriginalAvatarPrefabPath;
            public string MergeAnimatorDiffJson;
            public string FbxGuid;
            public string FbxName;
            public string FaceMeshAssetPath;
        }

        internal static bool TryGetMergeAnimatorDiffJson(string chibiPrefabPath, string originalAvatarPrefabPath, out string mergeAnimatorDiffJson)
        {
            mergeAnimatorDiffJson = string.Empty;
            if (string.IsNullOrEmpty(chibiPrefabPath) || string.IsNullOrEmpty(originalAvatarPrefabPath)) return false;

            EnsureFaceMeshCacheLoaded();
            if (!CachedFaceMeshByPrefab.TryGetValue(chibiPrefabPath, out var cached)) return false;

            var signature = cached.FaceMeshSignature;
            if (!string.Equals(signature.OriginalAvatarPrefabPath, originalAvatarPrefabPath, StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(signature.MergeAnimatorDiffJson)) return false;

            mergeAnimatorDiffJson = signature.MergeAnimatorDiffJson;
            return true;
        }

        internal static void SaveMergeAnimatorDiffJson(string chibiPrefabPath, string originalAvatarPrefabPath, string mergeAnimatorDiffJson)
        {
            if (string.IsNullOrEmpty(chibiPrefabPath) || string.IsNullOrEmpty(originalAvatarPrefabPath)) return;

            EnsureFaceMeshCacheLoaded();

            var hash = AssetDatabase.GetAssetDependencyHash(chibiPrefabPath);
            if (CachedFaceMeshByPrefab.TryGetValue(chibiPrefabPath, out var cached))
            {
                var updatedSignature = cached.FaceMeshSignature.WithMergeAnimatorDiff(originalAvatarPrefabPath, mergeAnimatorDiffJson ?? string.Empty);
                CachedFaceMeshByPrefab[chibiPrefabPath] = new CachedFaceMesh(hash, updatedSignature, cached.HasFaceMesh);
                MarkFaceMeshCacheDirty();
                return;
            }

            var emptySignature = new FaceMeshSignature(
                default,
                default,
                string.Empty,
                string.Empty,
                string.Empty,
                originalAvatarPrefabPath,
                mergeAnimatorDiffJson ?? string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            CachedFaceMeshByPrefab[chibiPrefabPath] = new CachedFaceMesh(hash, emptySignature, hasFaceMesh: false);
            MarkFaceMeshCacheDirty();
        }
    }
}
#endif
