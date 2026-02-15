#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OchibiChansConverterToolEditorUtility.cs
//
// ============================================================================
// 概要
// ============================================================================
// - Editor 拡張でよく使う処理をまとめたユーティリティです（他ツールでも再利用しやすい）
// - 分割しすぎて迷子にならないように「Editor 一般処理」を 1 ファイルに集約しています
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - Runtime（実行時）で使うコードはここに置きません（Editor 専用）
// - 参照リマップは PrefabContents → Scene の付け替えで重要です（誤るとリンク切れになります）
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - 変更前に「どのアセット/どの階層を触るか」をコメントに残す（事故防止）
// - Editor 拡張は必ず Undo を記録する（ユーザーが戻せることが最優先）
// - Prefab アセットを勝手に更新しない（Scene 上の対象だけを変更）
// - 処理順が仕様なので、並べ替える時は README とコメントも更新する
//
// ============================================================================

using System;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// Editor 拡張で共有する汎用処理（参照変換や階層探索など）を提供します。
    /// </summary>
    internal static class OchibiChansConverterToolEditorUtility
    {
        // --------------------------------------------------------------------
        // Ctrl+D 相当（Edit/Duplicate）
        // --------------------------------------------------------------------

        public const string UnityDuplicateMenuItemPath = "Edit/Duplicate";

        // --------------------------------------------------------------------
        // Transform 検索 / パス補正
        // --------------------------------------------------------------------

        /// <summary>
        /// AnimationUtility.CalculateTransformPath の結果（srcArmature 基準パス）を、
        /// dstArmature 基準で Find できる形に補正します。
        /// </summary>
        public static string NormalizeRelPathFor(Transform dstArmature, string relativePathFromSrcArmature)
        {
            if (dstArmature == null)
            {
                return relativePathFromSrcArmature;
            }

            if (string.IsNullOrEmpty(relativePathFromSrcArmature))
            {
                return relativePathFromSrcArmature;
            }

            // 例：src 側が "Armature/Hips/..." のように “Armature を含む” 形で返る場合に備える
            // dstArmature を基準に Find するため、先頭 "Armature/" は削る。
            var armatureName = dstArmature.name;

            if (relativePathFromSrcArmature == armatureName)
            {
                return string.Empty;
            }

            var prefix = armatureName + "/";
            if (relativePathFromSrcArmature.StartsWith(prefix, StringComparison.Ordinal))
            {
                return relativePathFromSrcArmature.Substring(prefix.Length);
            }

            // それ以外はそのまま
            return relativePathFromSrcArmature;
        }

        /// <summary>
        /// 子階層から名前一致で Transform を探します（最初に見つかったもの）。
        /// </summary>
        public static Transform FindTransformByNameRecursive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                var found = FindTransformByNameRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// アバターのメイン Armature を安全に探します。
        /// </summary>
        public static Transform FindAvatarMainArmature(Transform avatarRoot)
        {
            if (avatarRoot == null)
            {
                return null;
            }

            Animator animator = null;
            if (OchibiChansConverterToolVrcAvatarDescriptorUtility.TryGetAnimatorFromAvatar(avatarRoot.gameObject, out var vrcAnimator))
            {
                animator = vrcAnimator;
            }

            if (animator == null)
            {
                animator = avatarRoot.GetComponentInChildren<Animator>(true);
            }

            var armature = TryFindArmatureFromAnimator(avatarRoot, animator);
            if (armature != null)
            {
                return armature;
            }

            var byName = avatarRoot.Find("Armature") ?? FindTransformByNameRecursive(avatarRoot, "Armature");
            return byName;
        }

        private static Transform TryFindArmatureFromAnimator(Transform avatarRoot, Animator animator)
        {
            if (avatarRoot == null || animator == null)
            {
                return null;
            }

            if (!animator.isHuman)
            {
                return null;
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                return null;
            }

            if (!IsDescendantOf(hips, avatarRoot))
            {
                return null;
            }

            return GetTopmostChildUnderRoot(hips, avatarRoot);
        }

        private static bool IsDescendantOf(Transform target, Transform root)
        {
            for (var current = target; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform GetTopmostChildUnderRoot(Transform target, Transform root)
        {
            if (target == null || root == null)
            {
                return null;
            }

            var current = target;
            Transform last = target;
            while (current != null && current != root)
            {
                last = current;
                current = current.parent;
            }

            return current == root ? last : null;
        }

        // --------------------------------------------------------------------
        // 参照リマップ
        // --------------------------------------------------------------------

        /// <summary>
        /// PrefabContents 側（srcRoot）に向いている参照を、変換対象（dstRoot）側へ付け替えます。
        /// 例：srcRoot 配下の Transform / Component を参照している場合、同パスの dstRoot 配下参照へ差し替える。
        /// </summary>
        public static void RemapObjectReferencesInObject(UnityEngine.Object target, GameObject srcRoot, GameObject dstRoot)
        {
            if (target == null)
            {
                return;
            }

            if (srcRoot == null || dstRoot == null)
            {
                return;
            }

            var so = new SerializedObject(target);
            var prop = so.GetIterator();

            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                var objRef = prop.objectReferenceValue;
                if (objRef == null)
                {
                    continue;
                }

                var mapped = MapObjectReference(objRef, srcRoot, dstRoot);
                if (mapped == null)
                {
                    continue;
                }

                if (mapped == objRef)
                {
                    continue;
                }

                prop.objectReferenceValue = mapped;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// srcRoot 配下参照なら、同パスの dstRoot 配下参照へ変換します。
        /// </summary>
        /// <remarks>
        /// 注意：
        /// - “Hierarchy の構造が同じ” であることが前提です（同じパスに同じ名前の Transform がある）
        /// - 構造が違う場合は変換できないので null を返します（元参照は維持されます）
        ///
        /// PrefabContents を LoadPrefabContents で開くと、参照先は PrefabContents 側を指しがちです。
        /// Scene 側（複製されたオブジェクト）へ付け替えたいときにこの処理を使います。
        /// </remarks>
        /// <para>変換できない場合は null を返します（元参照を維持したいので “null” と区別します）。</para>
        public static UnityEngine.Object MapObjectReference(UnityEngine.Object srcRef, GameObject srcRoot, GameObject dstRoot)
        {
            if (srcRef == null)
            {
                return null;
            }

            if (srcRoot == null || dstRoot == null)
            {
                return null;
            }

            // Transform
            if (srcRef is Transform srcT)
            {
                if (!IsChildOf(srcT, srcRoot.transform))
                {
                    return null;
                }

                string rel = AnimationUtility.CalculateTransformPath(srcT, srcRoot.transform);
                if (string.IsNullOrEmpty(rel))
                {
                    return null;
                }

                var dstT = dstRoot.transform.Find(rel);
                return dstT != null ? dstT : null;
            }

            // Component
            if (srcRef is Component srcC)
            {
                var srcGO = srcC.gameObject;
                if (srcGO == null)
                {
                    return null;
                }

                if (!IsChildOf(srcGO.transform, srcRoot.transform))
                {
                    return null;
                }

                string rel = AnimationUtility.CalculateTransformPath(srcGO.transform, srcRoot.transform);
                if (string.IsNullOrEmpty(rel))
                {
                    return null;
                }

                var dstT = dstRoot.transform.Find(rel);
                if (dstT == null)
                {
                    return null;
                }

                var dstGO = dstT.gameObject;

                // 同じ型の Component が複数ある場合に備え、index を合わせて取得する
                var type = srcC.GetType();
                int srcIndex = GetComponentIndexByTypeOnGameObject(srcGO, type, srcC);
                var dstComps = dstGO.GetComponents(type);
                if (dstComps == null || dstComps.Length == 0)
                {
                    return null;
                }

                if (srcIndex < 0 || srcIndex >= dstComps.Length)
                {
                    return null;
                }

                return dstComps[srcIndex];
            }

            // GameObject
            if (srcRef is GameObject srcGORef)
            {
                if (!IsChildOf(srcGORef.transform, srcRoot.transform))
                {
                    return null;
                }

                string rel = AnimationUtility.CalculateTransformPath(srcGORef.transform, srcRoot.transform);
                if (string.IsNullOrEmpty(rel))
                {
                    return null;
                }

                var dstT = dstRoot.transform.Find(rel);
                return dstT != null ? dstT.gameObject : null;
            }

            return null;
        }

        /// <summary>
        /// child が parent の子孫かどうか。
        /// </summary>
        public static bool IsChildOf(Transform child, Transform parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }

            var t = child;
            while (t != null)
            {
                if (t == parent)
                {
                    return true;
                }

                t = t.parent;
            }

            return false;
        }

        /// <summary>
        /// 同一 GameObject 上に同じ型の Component が複数ある場合、
        /// “何番目の Component か” を返します。
        /// </summary>
        public static int GetComponentIndexByTypeOnGameObject(GameObject go, Type type, Component instance)
        {
            if (go == null || type == null || instance == null)
            {
                return -1;
            }

            var comps = go.GetComponents(type);
            if (comps == null)
            {
                return -1;
            }

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == instance)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
#endif
