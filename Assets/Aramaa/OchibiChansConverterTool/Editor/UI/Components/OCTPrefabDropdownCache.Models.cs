#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - FaceMesh 判定に必要な内部データモデルを定義する分割ファイルです。
// - 値オブジェクト（MeshId / FaceMeshSignature / CachedFaceMesh）を提供します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - ここはデータ定義のみで、AssetDatabase 参照や IO は行いません。
// - モデル追加時は「比較に使う識別子かどうか」を明確にしてから追加してください。
// ============================================================================

using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    internal sealed partial class OCTPrefabDropdownCache
    {
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
