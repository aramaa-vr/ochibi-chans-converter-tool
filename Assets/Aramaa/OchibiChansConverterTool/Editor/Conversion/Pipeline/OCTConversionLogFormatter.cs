#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTConversionLogFormatter.cs
//
// ============================================================================
// 概要
// ============================================================================
// - おちびちゃんズ変換のログ出力で使う文字列整形をまとめたユーティリティです
// - 値（数値）を出さず、名前やパスだけを一貫した形式で出力します
//
// ============================================================================
// 重要メモ（初心者向け）
// ============================================================================
// - Scene 上の参照はパスで表現し、値は出力しません
// - Prefab アセット参照は AssetDatabase からパスを取得します
//
// ============================================================================
// チーム開発向けルール
// ============================================================================
// - ログ文言やフォーマットを変える際は、出力先のツールも合わせて確認する
// - 値を出さない方針（プライバシー/安全）の維持を最優先する
//
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// 変換ログのフォーマットを共通化するユーティリティです。
    /// </summary>
    internal static class OCTConversionLogFormatter
    {
        /// <summary>
        /// Transform の階層パスを "Root/Child/..." 形式で返します。
        /// </summary>
        public static string GetHierarchyPath(Transform t)
        {
            if (t == null)
            {
                return OCTLocalizationService.Get("Log.NullValue");
            }

            var names = new List<string>(32);
            var cur = t;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>
        /// UnityEngine.Object の参照を「名前 + アセットパス」で整形します。
        /// </summary>
        public static string FormatAssetRef(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return OCTLocalizationService.Get("Log.NoneValue");
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                // Scene 上のオブジェクト等（今回はログ用なので名前だけで十分）
                return obj.name;
            }

            return $"{obj.name} ({path})";
        }
    }
}
#endif
