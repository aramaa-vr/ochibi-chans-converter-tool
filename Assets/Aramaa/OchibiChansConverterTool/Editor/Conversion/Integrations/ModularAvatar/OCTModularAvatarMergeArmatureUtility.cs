#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// ModularAvatarMergeArmature のボーン対応情報を、他処理で再利用しやすい形に変換するユーティリティ。
    /// </summary>
    internal static class OCTModularAvatarMergeArmatureUtility
    {
        internal sealed class BoneScaleMapping
        {
            public Transform OutfitBone;
            public string BaseBoneName;
            public Vector3 BaseScale;
        }

        internal static bool TryCollectBoneScaleMappings(Transform costumeRoot, List<BoneScaleMapping> mappings)
        {
            if (costumeRoot == null || mappings == null)
            {
                return false;
            }

#if !CHIBI_MODULAR_AVATAR
            return false;
#else
            var mergeArmatures = costumeRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true);
            if (mergeArmatures == null || mergeArmatures.Length == 0)
            {
                return false;
            }

            bool hadMappings = false;
            foreach (var mergeArmature in mergeArmatures)
            {
                if (mergeArmature == null)
                {
                    continue;
                }

                foreach (var pair in EnumerateMergeArmatureBoneMappings(mergeArmature))
                {
                    hadMappings = true;
                    if (pair.BaseBone == null || pair.OutfitBone == null)
                    {
                        continue;
                    }

                    mappings.Add(new BoneScaleMapping
                    {
                        OutfitBone = pair.OutfitBone,
                        BaseBoneName = pair.BaseBone.name,
                        BaseScale = pair.BaseBone.localScale
                    });
                }
            }

            return hadMappings;
#endif
        }

#if CHIBI_MODULAR_AVATAR
        private readonly struct MergeArmatureBonePair
        {
            public readonly Transform BaseBone;
            public readonly Transform OutfitBone;

            public MergeArmatureBonePair(Transform baseBone, Transform outfitBone)
            {
                BaseBone = baseBone;
                OutfitBone = outfitBone;
            }
        }

        private static IEnumerable<MergeArmatureBonePair> EnumerateMergeArmatureBoneMappings(ModularAvatarMergeArmature mergeArmature)
        {
            if (mergeArmature == null)
            {
                yield break;
            }

            var method = mergeArmature.GetType().GetMethod(
                "GetBonesMapping",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (method == null)
            {
                yield break;
            }

            IEnumerable enumerable;
            try
            {
                enumerable = method.Invoke(mergeArmature, null) as IEnumerable;
            }
            catch
            {
                // MA バージョン差分や実行時状態で Invoke が失敗しても、
                // 変換処理全体を止めずに安全にフォールバックさせる。
                yield break;
            }

            if (enumerable == null)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                if (!TryReadBonePair(item, out var baseBone, out var outfitBone))
                {
                    continue;
                }

                yield return new MergeArmatureBonePair(baseBone, outfitBone);
            }
        }

        private static bool TryReadBonePair(object item, out Transform baseBone, out Transform outfitBone)
        {
            baseBone = ReadTransformMember(item, "Item1", "baseBone", "BaseBone", "Source");
            outfitBone = ReadTransformMember(item, "Item2", "outfitBone", "OutfitBone", "Destination");
            return baseBone != null && outfitBone != null;
        }

        private static Transform ReadTransformMember(object obj, params string[] memberNames)
        {
            if (obj == null)
            {
                return null;
            }

            var type = obj.GetType();
            foreach (var name in memberNames)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && typeof(Transform).IsAssignableFrom(property.PropertyType))
                {
                    return property.GetValue(obj) as Transform;
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && typeof(Transform).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(obj) as Transform;
                }
            }

            return null;
        }
#endif
    }
}
#endif
