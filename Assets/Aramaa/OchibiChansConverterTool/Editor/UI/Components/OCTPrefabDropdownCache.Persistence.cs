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
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - キャッシュ JSON の互換性を壊す場合は、FaceMeshCacheFileName のバージョンを更新すること。
// - 例外は握り潰さず Warning ログを残し、処理を継続できる設計を維持すること。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal sealed partial class OCTPrefabDropdownCache
    {
        // NOTE: 永続化層は「失敗しても継続」方針です。
        // 読み込み/保存失敗は Warning ログのみを出して、候補検索自体は続行します。
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
            public string PrefabGuid;
            public string PrefabName;
            public string FbxGuid;
            public string FbxName;
            public string FaceMeshAssetPath;
        }
    }
}
#endif
