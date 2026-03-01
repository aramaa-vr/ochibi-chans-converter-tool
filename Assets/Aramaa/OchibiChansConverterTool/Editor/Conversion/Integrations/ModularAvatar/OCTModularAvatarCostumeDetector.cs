#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - MA Mesh Settings を起点に、衣装候補 Transform を収集するクラスです。
// - 値変更は行わず「検出専用」に徹します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - MA への直接参照は使わず、反射で型解決して走査します。
// - MA 未導入時は空リストを返すだけで安全に終了します。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - ここに適用ロジック（scale/BlendShape 変更）を入れない。
// - 検出条件を変える場合はログ・互換性影響を別 PR で明確化する。
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA Mesh Settings を起点に衣装ルート候補を抽出します。
    /// </summary>
    internal static class OCTModularAvatarCostumeDetector
    {
        /// <summary>
        /// dstRoot 配下の衣装候補（重複なし）を返します。
        /// </summary>
        internal static List<Transform> CollectCostumeRoots(GameObject dstRoot)
        {
            var costumeRoots = new List<Transform>();
            if (dstRoot == null)
            {
                return costumeRoots;
            }

            if (!OCTModularAvatarReflection.TryGetMeshSettingsType(out var meshSettingsType))
            {
                return costumeRoots;
            }

            costumeRoots = OCTModularAvatarReflection
                .GetComponentsInChildren(dstRoot, meshSettingsType, includeInactive: true)
                .Select(c => c != null ? c.transform : null)
                .Where(t => t != null && t.gameObject != dstRoot)
                .Distinct()
                .ToList();

            return costumeRoots;
        }
    }
}
#endif
