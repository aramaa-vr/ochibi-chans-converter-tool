#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - FaceMesh の抽出・一致判定を担当する分割ファイルです。
// - 候補 Prefab が「同じ顔メッシュ系統か」を判定するロジックをまとめています。
// - OriginalAvatarPrefabPath（元アバタープリファブのパス）もここで算出します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - ここは「比較/抽出」のみを扱い、UI や候補一覧の状態は変更しません。
// - VRChat SDK が無い環境では #if で安全にスキップされます。
// - OriginalAvatarPrefabPath は「Variant 系譜を上流へたどって最初に条件一致した .prefab」を採用します。
//   （BaseFolder 配下は除外、ルートに Descriptor があることが条件）
// ============================================================================

using System;
using UnityEditor;
using UnityEngine;
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
            _ = TryGetOriginalAvatarPrefabPath(root, out var originalAvatarPrefabPath);

            _ = TryGetAnimatorAvatarInfoFromDescriptor(descriptor, out var avatarId, out var avatarAssetPath);
            return TryBuildFaceMeshSignature(
                mesh,
                avatarId,
                avatarAssetPath,
                prefabGuid,
                prefabName,
                originalAvatarPrefabPath,
                out signature);
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

            if (AvatarIdMatches(a.AvatarId, b.AvatarId)) return true;

            if (AvatarAssetPathMatches(a, b)) return true;

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

        private static bool HasStrongAvatarId(FaceMeshSignature signature)
        {
            return !string.IsNullOrEmpty(signature.AvatarId.Guid) && signature.AvatarId.HasLocalId;
        }

        private static bool AvatarIdMatches(MeshId a, MeshId b)
        {
            if (string.IsNullOrEmpty(a.Guid) || string.IsNullOrEmpty(b.Guid)) return false;
            if (!string.Equals(a.Guid, b.Guid, StringComparison.Ordinal)) return false;

            if (a.HasLocalId && b.HasLocalId)
            {
                return a.LocalId == b.LocalId;
            }

            return false;
        }

        private static bool AvatarAssetPathMatches(FaceMeshSignature a, FaceMeshSignature b)
        {
            if (HasStrongAvatarId(a) || HasStrongAvatarId(b)) return false;
            if (string.IsNullOrEmpty(a.AvatarAssetPath) || string.IsNullOrEmpty(b.AvatarAssetPath)) return false;
            return string.Equals(a.AvatarAssetPath, b.AvatarAssetPath, StringComparison.OrdinalIgnoreCase);
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
            MeshId avatarId,
            string avatarAssetPath,
            string prefabGuid,
            string prefabName,
            string originalAvatarPrefabPath,
            out FaceMeshSignature signature)
        {
            signature = default;
            if (mesh == null) return false;

            var assetPath = AssetDatabase.GetAssetPath(mesh);
            var fbxGuid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
            var fbxName = string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath;
            var hasMeshId = TryBuildMeshId(mesh, out var meshId);

            if (!hasMeshId &&
                string.IsNullOrEmpty(avatarId.Guid) &&
                string.IsNullOrEmpty(avatarAssetPath) &&
                string.IsNullOrEmpty(prefabGuid) &&
                string.IsNullOrEmpty(prefabName) &&
                string.IsNullOrEmpty(fbxGuid) &&
                string.IsNullOrEmpty(fbxName) &&
                string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            signature = new FaceMeshSignature(
                meshId,
                avatarId,
                avatarAssetPath,
                prefabGuid,
                prefabName,
                originalAvatarPrefabPath,
                fbxGuid,
                fbxName,
                assetPath);
            return true;
        }

        private static bool TryGetOriginalAvatarPrefabPath(GameObject root, out string originalAvatarPrefabPath)
        {
            originalAvatarPrefabPath = string.Empty;
            if (root == null) return false;

            // 入口:
            // - Prefab Instance なら「現在適用されている Prefab Asset」から開始
            // - Prefab Asset ならその Asset 自身から開始
            var currentPrefabAsset = ResolveLineageStartPrefabAsset(root);
            while (currentPrefabAsset != null)
            {
                var currentPath = AssetDatabase.GetAssetPath(currentPrefabAsset);
                // 条件を満たす最初の候補を採用する（要件準拠）。
                if (IsOriginalAvatarPrefabPathCandidate(currentPath) &&
                    PrefabAssetRootHasDescriptor(currentPrefabAsset))
                {
                    originalAvatarPrefabPath = TryFindKisekaePrefabPathInSameDirectory(currentPath) ?? currentPath;
                    return true;
                }

                // Variant 系譜の上流（Base側）へ 1段ずつ遡る。
                var nextPrefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(currentPrefabAsset);
                if (nextPrefabAsset == currentPrefabAsset) break;
                currentPrefabAsset = nextPrefabAsset;
            }

            return false;
        }

        private static string TryFindKisekaePrefabPathInSameDirectory(string prefabPath)
        {
            // 候補列挙・名前優先順位は共通ユーティリティへ集約。
            // ここでは「元アバター候補として有効か」の条件だけを渡す。
            return OCTPrefabPathSelectionUtility.FindPreferredKisekaeSiblingPrefabPath(
                prefabPath,
                path => IsOriginalAvatarPrefabPathCandidate(path) && PrefabPathHasDescriptor(path));
        }

        private static bool PrefabPathHasDescriptor(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return PrefabAssetRootHasDescriptor(prefabAsset);
        }

        /// <summary>
        /// Variant 系譜探索の開始地点となる Prefab Asset を決めます。
        /// - Prefab Instance: インスタンスの適用元 Prefab Asset を返す
        /// - Prefab Asset: そのアセット自身を返す
        /// - それ以外: 系譜を辿れないため null
        /// </summary>
        private static GameObject ResolveLineageStartPrefabAsset(GameObject root)
        {
            if (root == null) return null;

            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
                if (instanceRoot == null) return null;
                return PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            }

            if (PrefabUtility.IsPartOfPrefabAsset(root))
            {
                return root;
            }

            return null;
        }

        private static bool IsOriginalAvatarPrefabPathCandidate(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return false;
            // おちびちゃんズ配布側（BaseFolder）を除外し、元アバター側のみ対象にする。
            if (prefabPath.StartsWith(BaseFolder + "/", StringComparison.OrdinalIgnoreCase)) return false;
            return !string.Equals(prefabPath, BaseFolder, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PrefabAssetRootHasDescriptor(GameObject prefabAsset)
        {
            if (prefabAsset == null) return false;
#if VRC_SDK_VRCSDK3
            return prefabAsset.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null;
#else
            return false;
#endif
        }

        private static bool TryBuildMeshId(Mesh mesh, out MeshId meshId)
        {
            meshId = default;
            if (mesh == null) return false;

            return TryBuildAssetObjectId(mesh, out meshId);
        }

        private static bool TryBuildAssetObjectId(UnityEngine.Object assetObject, out MeshId meshId)
        {
            meshId = default;
            if (assetObject == null) return false;

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetObject, out var guid, out long localId))
            {
                meshId = new MeshId(guid, localId, hasLocalId: true);
                return true;
            }

            var assetPath = AssetDatabase.GetAssetPath(assetObject);
            if (string.IsNullOrEmpty(assetPath)) return false;

            var fallbackGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(fallbackGuid)) return false;

            meshId = new MeshId(fallbackGuid, 0, hasLocalId: false);
            return true;
        }

#if VRC_SDK_VRCSDK3
        private static bool TryGetAnimatorAvatarInfoFromDescriptor(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor descriptor,
            out MeshId avatarId,
            out string avatarAssetPath)
        {
            avatarId = default;
            avatarAssetPath = string.Empty;
            if (descriptor == null) return false;

            // Descriptor が付いている同一 GameObject の Animator/Avatar だけを使う。
            // 親子や別オブジェクトまで広げると、逆引き候補の誤一致が増えるため。
            var animator = descriptor.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                // 仕様: ここで false は「Avatar 条件としては不成立」を意味する。
                // FaceMesh 判定処理全体は呼び出し元で継続する。
                return false;
            }

            var avatar = animator.avatar;
            var hasAvatarId = TryBuildAssetObjectId(avatar, out avatarId);
            avatarAssetPath = AssetDatabase.GetAssetPath(avatar) ?? string.Empty;
            return hasAvatarId || !string.IsNullOrEmpty(avatarAssetPath);
        }

        private static bool TryGetVisemeRendererFromDescriptor(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor descriptor,
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
