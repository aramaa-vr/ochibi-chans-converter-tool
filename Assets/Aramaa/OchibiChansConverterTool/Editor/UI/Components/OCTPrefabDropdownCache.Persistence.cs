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
using System.Linq;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal sealed partial class OCTPrefabDropdownCache
    {
        private readonly struct MergeAnimatorDiffCacheKey : IEquatable<MergeAnimatorDiffCacheKey>
        {
            /// <summary>
            /// MergeAnimator差分キャッシュの複合キーを初期化します。
            /// </summary>
            public MergeAnimatorDiffCacheKey(string chibiPrefabPath, string originalAvatarPrefabPath)
            {
                ChibiPrefabPath = chibiPrefabPath ?? string.Empty;
                OriginalAvatarPrefabPath = originalAvatarPrefabPath ?? string.Empty;
            }

            public string ChibiPrefabPath { get; }
            public string OriginalAvatarPrefabPath { get; }

            /// <summary>
            /// 複合キー同士を完全一致（Ordinal）で比較します。
            /// </summary>
            public bool Equals(MergeAnimatorDiffCacheKey other)
            {
                return string.Equals(ChibiPrefabPath, other.ChibiPrefabPath, StringComparison.Ordinal) &&
                       string.Equals(OriginalAvatarPrefabPath, other.OriginalAvatarPrefabPath, StringComparison.Ordinal);
            }

            /// <summary>
            /// object 比較用の Equals 実装です。
            /// </summary>
            public override bool Equals(object obj)
            {
                return obj is MergeAnimatorDiffCacheKey other && Equals(other);
            }

            /// <summary>
            /// Dictionary キーとして使うためのハッシュ値を返します。
            /// </summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((ChibiPrefabPath != null ? ChibiPrefabPath.GetHashCode() : 0) * 397) ^
                           (OriginalAvatarPrefabPath != null ? OriginalAvatarPrefabPath.GetHashCode() : 0);
                }
            }
        }

        private static readonly Dictionary<MergeAnimatorDiffCacheKey, string> MergeAnimatorDiffJsonByPrefabPair =
            new Dictionary<MergeAnimatorDiffCacheKey, string>();

        /// <summary>
        /// Library 配下の FaceMeshCache(v11) を読み込み、FaceMesh情報と MergeAnimator差分を復元します。
        /// </summary>
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

                CachedFaceMeshByPrefab.Clear();
                MergeAnimatorDiffJsonByPrefabPair.Clear();

                // FaceMesh 本体キャッシュを復元
                if (cacheFile.Entries != null)
                {
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
                            entry.FbxGuid ?? string.Empty,
                            entry.FbxName ?? string.Empty,
                            entry.FaceMeshAssetPath ?? string.Empty);
                        CachedFaceMeshByPrefab[entry.PrefabPath] = new CachedFaceMesh(hash, signature, entry.HasFaceMesh);
                    }
                }

                // MergeAnimator 差分キャッシュを復元
                if (cacheFile.MergeAnimatorDiffEntries != null)
                {
                    foreach (var entry in cacheFile.MergeAnimatorDiffEntries)
                    {
                        if (entry == null) continue;
                        if (string.IsNullOrEmpty(entry.ChibiPrefabPath)) continue;
                        if (string.IsNullOrEmpty(entry.OriginalAvatarPrefabPath)) continue;

                        var key = new MergeAnimatorDiffCacheKey(entry.ChibiPrefabPath, entry.OriginalAvatarPrefabPath);
                        string mergeAnimatorDiffJson = null;
                        if (entry.MergeAnimatorDiff != null)
                        {
                            mergeAnimatorDiffJson = JsonUtility.ToJson(entry.MergeAnimatorDiff, false);
                        }
                        else if (!string.IsNullOrEmpty(entry.MergeAnimatorDiffJson))
                        {
                            // 旧形式（JSON文字列埋め込み）の後方読込。
                            mergeAnimatorDiffJson = entry.MergeAnimatorDiffJson;
                        }

                        if (string.IsNullOrEmpty(mergeAnimatorDiffJson)) continue;
                        MergeAnimatorDiffJsonByPrefabPair[key] = mergeAnimatorDiffJson;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("Warning.FaceMeshCacheLoadFailed", e.Message));
            }
        }

        /// <summary>
        /// 現在メモリ上にある FaceMesh キャッシュと MergeAnimator 差分を Library 配下へ保存します。
        /// </summary>
        private static void SaveFaceMeshCacheToLibrary()
        {
            if (!_faceMeshCacheDirty) return;

            try
            {
                var cacheFile = new FaceMeshCacheFile();
                foreach (var pair in CachedFaceMeshByPrefab.OrderBy(p => p.Key, StringComparer.Ordinal))
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
                        FbxGuid = cached.FaceMeshSignature.FbxGuid,
                        FbxName = cached.FaceMeshSignature.FbxName,
                        FaceMeshAssetPath = cached.FaceMeshSignature.FaceMeshAssetPath,
                        HasFaceMesh = cached.HasFaceMesh
                    });
                }

                foreach (var pair in MergeAnimatorDiffJsonByPrefabPair
                             .OrderBy(p => p.Key.ChibiPrefabPath, StringComparer.Ordinal)
                             .ThenBy(p => p.Key.OriginalAvatarPrefabPath, StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(pair.Value))
                    {
                        continue;
                    }

                    var mergeAnimatorDiff = TryParseMergeAnimatorDiffPayload(pair.Value);
                    if (mergeAnimatorDiff == null)
                    {
                        continue;
                    }

                    cacheFile.MergeAnimatorDiffEntries.Add(new MergeAnimatorDiffCacheEntry
                    {
                        ChibiPrefabPath = pair.Key.ChibiPrefabPath,
                        OriginalAvatarPrefabPath = pair.Key.OriginalAvatarPrefabPath,
                        MergeAnimatorDiff = mergeAnimatorDiff
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

        /// <summary>
        /// FaceMeshCache ファイルの保存先パスを返します。
        /// </summary>
        private static string GetFaceMeshCacheFilePath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "Library", "Aramaa", "OchibiChansConverterTool", FaceMeshCacheFileName);
        }

        /// <summary>
        /// 文字列から Hash128 を安全にパースします。
        /// </summary>
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
            public List<MergeAnimatorDiffCacheEntry> MergeAnimatorDiffEntries = new List<MergeAnimatorDiffCacheEntry>();
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
            public string FbxGuid;
            public string FbxName;
            public string FaceMeshAssetPath;
        }

        [Serializable]
        private sealed class MergeAnimatorDiffCacheEntry
        {
            public string ChibiPrefabPath;
            public string OriginalAvatarPrefabPath;
            public MergeAnimatorDiffPayload MergeAnimatorDiff;
            public string MergeAnimatorDiffJson;
        }

        [Serializable]
        private sealed class MergeAnimatorDiffPayload
        {
            public List<MergeAnimatorDiffItem> items = new List<MergeAnimatorDiffItem>();
        }

        [Serializable]
        private sealed class MergeAnimatorDiffItem
        {
            public string objectFullPath;
            public int componentIndex;
            public string sourceGuid;
            public string targetGuid;
        }

        /// <summary>
        /// (おちびPrefabPath, 元アバターPrefabPath) キーで MergeAnimator 差分JSONを取得します。
        /// </summary>
        internal static bool TryGetMergeAnimatorDiffJson(string chibiPrefabPath, string originalAvatarPrefabPath, out string mergeAnimatorDiffJson)
        {
            mergeAnimatorDiffJson = string.Empty;
            if (string.IsNullOrEmpty(chibiPrefabPath) || string.IsNullOrEmpty(originalAvatarPrefabPath)) return false;

            EnsureFaceMeshCacheLoaded();
            var key = new MergeAnimatorDiffCacheKey(chibiPrefabPath, originalAvatarPrefabPath);
            return MergeAnimatorDiffJsonByPrefabPair.TryGetValue(key, out mergeAnimatorDiffJson) &&
                   !string.IsNullOrEmpty(mergeAnimatorDiffJson);
        }

        /// <summary>
        /// (おちびPrefabPath, 元アバターPrefabPath) キーで MergeAnimator 差分JSONを保存します。
        /// </summary>
        internal static void SaveMergeAnimatorDiffJson(string chibiPrefabPath, string originalAvatarPrefabPath, string mergeAnimatorDiffJson)
        {
            if (string.IsNullOrEmpty(chibiPrefabPath) || string.IsNullOrEmpty(originalAvatarPrefabPath)) return;

            EnsureFaceMeshCacheLoaded();
            var key = new MergeAnimatorDiffCacheKey(chibiPrefabPath, originalAvatarPrefabPath);
            MergeAnimatorDiffJsonByPrefabPair[key] = mergeAnimatorDiffJson ?? string.Empty;
            MarkFaceMeshCacheDirty();
        }

        /// <summary>
        /// 保存済み MergeAnimator 差分キーから、originalAvatarPrefabPath に対応する chibiPrefabPath を逆引きします。
        /// 一意に決まる場合のみ true を返します。
        /// </summary>
        internal static bool TryResolveChibiPrefabPathFromStoredMergeDiff(string originalAvatarPrefabPath, out string chibiPrefabPath)
        {
            chibiPrefabPath = string.Empty;
            if (string.IsNullOrEmpty(originalAvatarPrefabPath))
            {
                return false;
            }

            EnsureFaceMeshCacheLoaded();
            var matched = MergeAnimatorDiffJsonByPrefabPair.Keys
                .Where(k => string.Equals(k.OriginalAvatarPrefabPath, originalAvatarPrefabPath, StringComparison.Ordinal))
                .Select(k => k.ChibiPrefabPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (matched.Length != 1)
            {
                // 意図:
                // - 一意に決まらない場合は誤適用を避けるため必ず失敗として扱う。
                // - ただし「なぜ復元できなかったか」を追跡できるよう、候補数と候補値を明示ログへ残す。
                Debug.LogWarning(
                    $"[OCT][MA MergeAnimator Diff] Chibi prefab reverse lookup is ambiguous. original={originalAvatarPrefabPath}, candidates={matched.Length}, values=[{string.Join(", ", matched)}]");
                return false;
            }

            chibiPrefabPath = matched[0];
            return true;
        }

        /// <summary>
        /// 文字列JSONを MergeAnimator 差分DTOへ変換します。
        /// 失敗時は null を返します。
        /// </summary>
        private static MergeAnimatorDiffPayload TryParseMergeAnimatorDiffPayload(string mergeAnimatorDiffJson)
        {
            if (string.IsNullOrEmpty(mergeAnimatorDiffJson))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<MergeAnimatorDiffPayload>(mergeAnimatorDiffJson);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
#endif
