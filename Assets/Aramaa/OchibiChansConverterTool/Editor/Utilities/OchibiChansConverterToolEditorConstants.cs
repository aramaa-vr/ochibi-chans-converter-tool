#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OchibiChansConverterToolEditorConstants.cs
//
// =====================================================================
// 概要
// =====================================================================
// - Editor 拡張で共通利用する定数（バージョン・URL 等）をまとめたクラスです。
// - 特殊な文字列や数字を一箇所に集約し、変更点を追いやすくします。
//
// =====================================================================

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// OchibiChansConverterTool の Editor 共有定数をまとめます。
    /// </summary>
    internal static class OchibiChansConverterToolEditorConstants
    {
        public const string ToolVersion = "0.5.4";
        public const string LatestVersionUrl = "https://aramaa-vr.github.io/vpm-repos/latest.json";
        public const string TargetPackageId = "jp.aramaa.ochibi-chans-converter-tool";
        public const int LatestJsonSchemaVersion = 1;
        public const string ToolWebsiteUrl = "https://aramaa-vr.github.io/ochibi-chans-converter-tool/";
        public const string ToolsMenuPath = "Tools/Aramaa/おちびちゃんズ化ツール（Ochibi-chans Converter Tool）";
        public const string GameObjectMenuPath = "GameObject/Aramaa/おちびちゃんズ化ツール（Ochibi-chans Converter Tool）";
        public const string FaceMeshCacheFileName = "ChibiFaceMeshCache.json";
        public const string BaseFolder = "Assets/夕時茶屋";
        public const string AddMenuPrefabFileName = "Ochibichans_Addmenu.prefab";
        public const string AddMenuNameKeyword = "Ochibichans_Addmenu";
    }
}
#endif
