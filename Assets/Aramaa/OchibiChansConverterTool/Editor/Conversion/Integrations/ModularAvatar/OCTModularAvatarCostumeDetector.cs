#if UNITY_EDITOR
// ============================================================================
// 概要
// ============================================================================
// - MA Mesh Settings を使って「衣装ルート候補」を検出する、MA依存クラスです。
// - 検出だけに責務を限定し、スケール調整は別クラスへ委譲します。
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - MA へのコンパイル時依存を避けるため、型参照はリフレクションで行います。
// - 検出結果（Transform一覧）は「入力データ」であり、ここでは値変更をしません。
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - MAコンポーネントの判定条件を変更する場合は、処理対象の意図をコメントに残す。
// - ここにスケール適用ロジックを混ぜない（責務分離を維持）。
// ============================================================================
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// MA Mesh Settings を起点に衣装ルート候補を抽出する責務を持つクラス。
    /// </summary>
    internal static class OCTModularAvatarCostumeDetector
    {
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
