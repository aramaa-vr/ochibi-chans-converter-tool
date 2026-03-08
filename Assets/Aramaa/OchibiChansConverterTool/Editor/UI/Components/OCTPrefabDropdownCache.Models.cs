#if UNITY_EDITOR
using System;
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
