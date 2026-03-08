#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - FaceMesh の抽出・一致判定を担当する分割ファイルです。
// - 候補 Prefab が「同じ顔メッシュ系統か」を判定するロジックをまとめています。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - ここは「比較/抽出」のみを扱い、UI や候補一覧の状態は変更しません。
// - VRChat SDK が無い環境では #if で安全にスキップされます。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 一致条件を変更する場合は、既存の優先順位（MeshId > GUID/Path）を維持するか理由を残すこと。
// - 新しい比較キーを追加する場合は、誤マッチのリスクをコメントに明記すること。
// ============================================================================
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal sealed partial class OCTPrefabDropdownCache
    {
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
    }
}
#endif
