#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
using nadena.dev.modular_avatar.core;
#endif

namespace Aramaa.OchibiChansConverterTool.Editor
{
    /// <summary>
    /// Modular Avatar の Floor Adjuster に関する追加処理をまとめます。
    ///
    /// Armature 配下の Component は、従来の
    /// <c>OCTConversionPipeline.AddMissingComponentsUnderArmature</c> が
    /// Component の型を問わず同期します。そのため、このクラスでは旧式
    /// FloorAdjuster の同期ロジックを再実装せず、Armature 外にある
    /// ModularAvatarFloorAdjuster の不足分だけを補います。
    /// また、復元時には変換先に残った ModularAvatarFloorAdjuster を除去します。
    /// </summary>
    internal static class OCTModularAvatarFloorAdjusterUtility
    {
        /// <summary>
        /// 指定したローカライズキーに対応する文字列を取得します。
        /// Undo 操作名など、引数を持たない表示文言に使用します。
        /// </summary>
        private static string L(string key) => OCTLocalization.Get(key);

        /// <summary>
        /// 指定したローカライズキーに引数を適用した文字列を取得します。
        /// 復元ログなど、階層パスや Component 名を含む文言に使用します。
        /// </summary>
        private static string F(string key, params object[] args) => OCTLocalization.Format(key, args);

        /// <summary>
        /// Armature 外にある ModularAvatarFloorAdjuster の不足分を変換先へ追加します。
        /// </summary>
        /// <remarks>
        /// Armature 配下は旧来の汎用 Component 同期処理が担当するため、このメソッドでは除外します。
        /// 変換先に対象 Transform が存在する場合は Component だけを追加します。
        /// Transform 自体が存在しない場合は、親 Transform が存在するときに限り対象の末端 GameObject を作成します。
        /// 親階層を再帰的に作成することや、既存 Transform / Component の上書きは行いません。
        /// </remarks>
        /// <param name="srcRoot">変換元のルートです。</param>
        /// <param name="dstRoot">変換先のルートです。</param>
        /// <param name="srcArmature">変換元のメイン Armature です。</param>
        /// <param name="logs">変換ログの出力先です。</param>
        internal static void CopyModularAvatarFloorAdjustersOutsideArmature(
            GameObject srcRoot,
            GameObject dstRoot,
            Transform srcArmature,
            List<string> logs
        )
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            // Modular Avatar 1.17.0 以降で Floor Adjuster 型が利用可能な場合だけ実行します。
            if (srcRoot == null || dstRoot == null)
            {
                return;
            }

            // 呼び出し元では通常同じログリストが渡されますが、単独呼び出しでも安全にします。
            logs ??= new List<string>();
            var log = new OCTConversionLogger(logs);

            // 非アクティブな GameObject に付いた Component も変換対象に含めます。
            var sourceTransforms = srcRoot.GetComponentsInChildren<Transform>(includeInactive: true);

            foreach (var sourceTransform in sourceTransforms)
            {
                // 破棄済み参照、または旧来の Armature 配下はここでは扱いません。
                // Armature 配下は既存の汎用 Component 同期処理で処理されます。
                if (sourceTransform == null || IsUnderTransform(sourceTransform, srcArmature))
                {
                    continue;
                }

                // このメソッドの対象は MA Floor Adjuster のみです。
                // 旧式 FloorAdjuster を型名で探したり複製したりしないことで、旧来の処理経路を維持します。
                var sourceComponent = sourceTransform.GetComponent<ModularAvatarFloorAdjuster>();
                if (sourceComponent == null)
                {
                    continue;
                }

                // 変換元ルートからの相対パスを使い、変換先でも同じ階層位置を探します。
                var relativePath = AnimationUtility.CalculateTransformPath(sourceTransform, srcRoot.transform);
                var destinationTransform = string.IsNullOrEmpty(relativePath)
                    ? dstRoot.transform
                    : dstRoot.transform.Find(relativePath);
                GameObject createdObject = null;

                // 変換先に対象 Transform がない場合は、既存の親の直下に末端オブジェクトだけを作成します。
                if (destinationTransform == null)
                {
                    // 親の相対パスを求めます。sourceTransform がルート直下なら変換先ルートを親にします。
                    var parentPath = sourceTransform.parent == null
                        ? string.Empty
                        : AnimationUtility.CalculateTransformPath(sourceTransform.parent, srcRoot.transform);
                    var destinationParent = string.IsNullOrEmpty(parentPath)
                        ? dstRoot.transform
                        : dstRoot.transform.Find(parentPath);

                    // 親階層が変換先に存在しない場合は、階層全体を勝手に作らず、この Component をスキップします。
                    if (destinationParent == null)
                    {
                        continue;
                    }

                    // Undo で作成物を戻せるよう登録してから、変換先の親へ移動します。
                    createdObject = new GameObject(sourceTransform.name);
                    Undo.RegisterCreatedObjectUndo(createdObject, L("Undo.DuplicateApply"));
                    Undo.SetTransformParent(createdObject.transform, destinationParent, L("Undo.DuplicateApply"));

                    // 新規作成した末端オブジェクトには、変換元のローカル Transform と有効状態を適用します。
                    createdObject.transform.localPosition = sourceTransform.localPosition;
                    createdObject.transform.localRotation = sourceTransform.localRotation;
                    createdObject.transform.localScale = sourceTransform.localScale;
                    createdObject.SetActive(sourceTransform.gameObject.activeSelf);
                    destinationTransform = createdObject.transform;
                }

                // 変換先に同じ MA Floor Adjuster が既にある場合は、既存値を保護して何もしません。
                var destinationComponent = destinationTransform.GetComponent<ModularAvatarFloorAdjuster>();
                if (destinationComponent != null)
                {
                    continue;
                }

                // Component の追加を Undo 対象として実行します。
                // Unity 側で追加できない場合は、後続処理を行わずスキップします。
                try
                {
                    destinationComponent = Undo.AddComponent<ModularAvatarFloorAdjuster>(destinationTransform.gameObject);
                }
                catch
                {
                    destinationComponent = null;
                }

                if (destinationComponent == null)
                {
                    // このメソッドで作成した GameObject に Component を追加できなかった場合は、空のオブジェクトを残しません。
                    if (createdObject != null)
                    {
                        Undo.DestroyObjectImmediate(createdObject);
                    }

                    continue;
                }

                // Component の設定値を複製し、変換元階層を参照している場合は変換先階層へ付け替えます。
                // ModularAvatarFloorAdjuster 自体は現在フィールドを持ちませんが、将来の設定追加にも備えています。
                try
                {
                    EditorUtility.CopySerialized(sourceComponent, destinationComponent);
                    OCTEditorUtility.RemapObjectReferencesInObject(destinationComponent, srcRoot, dstRoot);
                }
                catch
                {
                    // 複製または参照リマップに失敗した場合は、追加した Component と新規オブジェクトを破棄します。
                    Undo.DestroyObjectImmediate(destinationComponent);
                    if (createdObject != null)
                    {
                        Undo.DestroyObjectImmediate(createdObject);
                    }

                    continue;
                }

                // Unity に変更を通知し、既存の Component 追加ログへ記録します。
                EditorUtility.SetDirty(destinationComponent);
                log.Add(
                    "Log.ComponentAdded",
                    nameof(ModularAvatarFloorAdjuster),
                    OCTConversionLogFormatter.GetHierarchyPath(destinationTransform)
                );
            }
#endif
        }

        /// <summary>
        /// 復元処理の対象アバターから ModularAvatarFloorAdjuster をすべて除去します。
        /// </summary>
        /// <remarks>
        /// 変換先は処理開始時に複製されたオブジェクトであるため、Undo 可能な形で除去します。
        /// 旧式 FloorAdjuster の除去は既存の <c>OCTRestoreModeProcessor</c> が担当します。
        /// </remarks>
        /// <param name="avatarRoot">復元対象アバターのルートです。</param>
        /// <param name="logs">復元ログの出力先です。</param>
        internal static void RemoveModularAvatarFloorAdjustersForRestore(GameObject avatarRoot, List<string> logs)
        {
#if CHIBI_MODULAR_AVATAR_FLOOR_ADJUSTER
            // MA Floor Adjuster 型が利用できる場合だけ実行します。
            if (avatarRoot == null)
            {
                return;
            }

            logs ??= new List<string>();

            // 非アクティブな階層も含め、復元対象の MA Floor Adjuster をすべて取得します。
            var adjusters = avatarRoot.GetComponentsInChildren<ModularAvatarFloorAdjuster>(includeInactive: true);
            foreach (var adjuster in adjusters)
            {
                // Destroy 済みなどの無効参照は無視します。
                if (adjuster == null)
                {
                    continue;
                }

                // 何をどこから削除したかを記録してから、Undo 対応で Component を削除します。
                logs.Add(F(
                    "Log.RestoreComponentRemoved",
                    nameof(ModularAvatarFloorAdjuster),
                    OCTConversionLogFormatter.GetHierarchyPath(adjuster.transform)
                ));
                Undo.DestroyObjectImmediate(adjuster);
            }
#endif
        }

        /// <summary>
        /// target が ancestor 自身またはその子孫かを判定します。
        /// </summary>
        /// <param name="target">判定対象の Transform です。</param>
        /// <param name="ancestor">祖先候補の Transform です。</param>
        /// <returns>target が ancestor 以下に存在する場合は true、それ以外は false です。</returns>
        private static bool IsUnderTransform(Transform target, Transform ancestor)
        {
            // どちらかが null の場合は、祖先関係なしとして扱います。
            if (target == null || ancestor == null)
            {
                return false;
            }

            // target から親へたどり、ancestor に到達すれば子孫と判定します。
            for (var current = target; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            // ルートまでたどっても ancestor に一致しなかった場合です。
            return false;
        }
    }
}
#endif
