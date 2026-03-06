#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar Merge Armature のボーン対応を利用して、衣装側ボーンの localScale を補正します。
    /// </summary>
    internal static class OCTModularAvatarCostumeScaleAdjuster
    {
        internal const float ScaleEpsilon = 0.0001f;
        private static string L(string key) => OCTLocalization.Get(key);

        internal static int AdjustByMergeArmatureMapping(GameObject dstRoot, List<string> logs = null)
        {
            return AdjustByMergeArmatureMapping(dstRoot, logs, out _);
        }

        internal static int AdjustByMergeArmatureMapping(GameObject dstRoot, List<string> logs, out int copiedScaleAdjusterCount)
        {
            copiedScaleAdjusterCount = 0;
            if (dstRoot == null)
            {
                return 0;
            }

#if CHIBI_MODULAR_AVATAR
            int appliedCount = 0;
            var mergers = dstRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true);

            foreach (var merger in mergers)
            {
                if (merger == null)
                {
                    continue;
                }

                var mappings = merger.GetBonesMapping();
                if (mappings == null || mappings.Count == 0)
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(merger.gameObject, L("Undo.AdjustCostumeScales"));

                foreach (var pair in mappings)
                {
                    var baseBone = pair.Item1;
                    var mergeBone = pair.Item2;
                    if (baseBone == null || mergeBone == null)
                    {
                        continue;
                    }

                    // Scale 値の適用有無に関係なく、必要なら ScaleAdjuster の複製を試みます。
                    // これにより baseBone の localScale が (1,1,1) でも、設定済みコンポーネントを衣装側へ反映できます。
                    copiedScaleAdjusterCount += CopyScaleAdjusterIfNeeded(baseBone, mergeBone, logs);

                    if (IsNearlyOne(baseBone.localScale))
                    {
                        continue;
                    }

                    if (!TryApplyScaleModifier(mergeBone, baseBone.localScale))
                    {
                        continue;
                    }

                    appliedCount++;

                    var baseBonePath = OCTConversionLogFormatter.GetHierarchyPath(baseBone);
                    var mergeBonePath = OCTConversionLogFormatter.GetHierarchyPath(mergeBone);

                    new OCTConversionLogger(logs).Add(
                        "Log.CostumeScaleApplied",
                        merger.name,
                        $"{baseBonePath}->{mergeBonePath}",
                        "ModularAvatarMergeArmature",
                        mergeBonePath
                    );

                }
            }

            if (appliedCount > 0)
            {
                logs?.Add($"[MA Merge Armature] Applied scale adjustments: {appliedCount}");
            }

            if (copiedScaleAdjusterCount > 0)
            {
                logs?.Add($"[MA Scale Adjuster] Copied components: {copiedScaleAdjusterCount}");
            }

            return appliedCount;
#else
            return 0;
#endif
        }

#if CHIBI_MODULAR_AVATAR
        /// <summary>
        /// マッピング元ボーンに ScaleAdjuster がある場合、マッピング先へ値ごと複製します。
        /// 既に先に同コンポーネントが付いている場合は上書きせず安全にスキップします。
        /// </summary>
        private static int CopyScaleAdjusterIfNeeded(Transform baseBone, Transform mergeBone, List<string> logs)
        {
            if (baseBone == null || mergeBone == null)
            {
                return 0;
            }

            var source = baseBone.GetComponent<ModularAvatarScaleAdjuster>();
            if (source == null)
            {
                return 0;
            }

            if (mergeBone.GetComponent<ModularAvatarScaleAdjuster>() != null)
            {
                return 0;
            }

            // ComponentUtility のコピーは内部バッファに依存するため、先にコピー可否を確認します。
            if (!ComponentUtility.CopyComponent(source))
            {
                logs?.Add($"[MA Scale Adjuster] Skip copy (copy buffer failed): {OCTConversionLogFormatter.GetHierarchyPath(baseBone)}");
                return 0;
            }

            // Paste 失敗時に中途半端な状態を残さないため、追加したコンポーネントを即座に破棄します。
            var added = Undo.AddComponent<ModularAvatarScaleAdjuster>(mergeBone.gameObject);
            if (added == null)
            {
                return 0;
            }

            if (!ComponentUtility.PasteComponentValues(added))
            {
                Undo.DestroyObjectImmediate(added);
                logs?.Add($"[MA Scale Adjuster] Skip copy (paste failed): {OCTConversionLogFormatter.GetHierarchyPath(mergeBone)}");
                return 0;
            }

            EditorUtility.SetDirty(mergeBone);

            var srcPath = OCTConversionLogFormatter.GetHierarchyPath(baseBone);
            var dstPath = OCTConversionLogFormatter.GetHierarchyPath(mergeBone);
            logs?.Add($"[MA Scale Adjuster] Copied: {srcPath} -> {dstPath}");

            return 1;
        }
#endif





        internal static bool TryApplyScaleModifier(Transform targetBone, Vector3 scaleModifier)
        {
            if (targetBone == null)
            {
                return false;
            }

            var before = targetBone.localScale;
            var adjusted = Vector3.Scale(before, scaleModifier);
            if (IsNearlyEqual(before, adjusted))
            {
                return false;
            }

            targetBone.localScale = adjusted;
            EditorUtility.SetDirty(targetBone);
            return true;
        }

        internal static bool IsNearlyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.y - 1f) < ScaleEpsilon
                   && Mathf.Abs(scale.z - 1f) < ScaleEpsilon;
        }

        internal static bool IsNearlyEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < ScaleEpsilon
                   && Mathf.Abs(a.y - b.y) < ScaleEpsilon
                   && Mathf.Abs(a.z - b.z) < ScaleEpsilon;
        }
    }
}
#endif
