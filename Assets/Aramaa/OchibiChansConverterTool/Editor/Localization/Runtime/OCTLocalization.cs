#if UNITY_EDITOR
// Assets/Aramaa/OchibiChansConverterTool/Editor/Utilities/OCTLocalization.cs
//
// =====================================================================
// 概要
// =====================================================================
// - OchibiChansConverterTool Editor 拡張の文字列を JSON から読み込むローカライザです。
// - OS 言語を初期値にしつつ、EditorPrefs で上書きできます。
//
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    internal static class OCTLocalization
    {
        private const string EditorPrefsKey = "Aramaa.OchibiChansConverterTool.Language";
        private const string LocalizationSubdirectory = "OchibiChansConverterTool";
        private const string LanguageJapanese = "ja";
        private const string LanguageEnglish = "en";
        private const string LanguageChineseSimplified = "zh-Hans";
        private const string LanguageChineseTraditional = "zh-Hant";
        private const string LanguageKorean = "ko-KR";

        private static readonly string[] LanguageCodes =
        {
            LanguageJapanese,
            LanguageEnglish,
            LanguageChineseSimplified,
            LanguageChineseTraditional,
            LanguageKorean
        };

        private static string _currentLanguageCode;
        private static string _loadedLanguageCode;
        private static Dictionary<string, string> _strings;
        private static string _cachedDisplayNamesLanguageCode;
        private static string[] _cachedDisplayNames;

        public static string CurrentLanguageCode
        {
            get
            {
                EnsureLanguageCode();
                return _currentLanguageCode;
            }
        }

        public static void SetLanguage(string languageCode)
        {
            EnsureLanguageCode();
            var normalized = NormalizeLanguage(languageCode);
            if (_currentLanguageCode == normalized)
            {
                return;
            }

            _currentLanguageCode = normalized;
            EditorPrefs.SetString(EditorPrefsKey, _currentLanguageCode);
            LoadStrings();
        }

        public static int GetLanguageIndex()
        {
            var current = CurrentLanguageCode;
            for (var i = 0; i < LanguageCodes.Length; i++)
            {
                if (string.Equals(LanguageCodes[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        public static string GetLanguageCodeFromIndex(int index)
        {
            if (index < 0 || index >= LanguageCodes.Length)
            {
                return LanguageEnglish;
            }

            return LanguageCodes[index];
        }

        public static string[] GetLanguageDisplayNames()
        {
            EnsureStrings();
            if (_cachedDisplayNames == null || _cachedDisplayNamesLanguageCode != _loadedLanguageCode)
            {
                _cachedDisplayNamesLanguageCode = _loadedLanguageCode;
                _cachedDisplayNames = new[]
                {
                    Get("Language.OptionJapanese"),
                    Get("Language.OptionEnglish"),
                    Get("Language.OptionChineseSimplified"),
                    Get("Language.OptionChineseTraditional"),
                    Get("Language.OptionKorean")
                };
            }

            return _cachedDisplayNames;
        }

        public static string Get(string key)
        {
            EnsureStrings();
            if (_strings != null && _strings.TryGetValue(key, out var value))
            {
                return value;
            }

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Get(key);
            }

            return string.Format(Get(key), args);
        }

        private static void EnsureLanguageCode()
        {
            if (!string.IsNullOrEmpty(_currentLanguageCode))
            {
                return;
            }

            var stored = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            _currentLanguageCode = NormalizeLanguage(string.IsNullOrEmpty(stored) ? GetSystemLanguageCode() : stored);
            EditorPrefs.SetString(EditorPrefsKey, _currentLanguageCode);
        }

        private static void EnsureStrings()
        {
            EnsureLanguageCode();
            if (_strings != null && _loadedLanguageCode == _currentLanguageCode)
            {
                return;
            }

            LoadStrings();
        }

        private static void LoadStrings()
        {
            _loadedLanguageCode = _currentLanguageCode;
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);

            LoadStringsFromLanguage(_currentLanguageCode);
        }

        private static void LoadStringsFromLanguage(string languageCode)
        {
            var localizationRoot = GetLocalizationRootPath();
            var jsonPath = Path.Combine(localizationRoot, $"strings.{languageCode}.json");

            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[OchibiChansConverterTool] Localization file missing: {jsonPath}");
                if (!string.Equals(languageCode, LanguageEnglish, StringComparison.OrdinalIgnoreCase))
                {
                    LoadStringsFromLanguage(LanguageEnglish);
                }
                return;
            }

            var json = File.ReadAllText(jsonPath);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var data = JsonUtility.FromJson<LocalizationData>(json);
            if (data?.entries == null)
            {
                return;
            }

            foreach (var entry in data.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key))
                {
                    continue;
                }

                _strings[entry.key] = entry.value ?? string.Empty;
            }
        }

        private static string GetLocalizationRootPath()
        {
            var candidates = new List<string>();
            var packageInfo = PackageInfo.FindForAssembly(typeof(OCTLocalization).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                candidates.Add(Path.Combine(
                    packageInfo.resolvedPath,
                    "Editor",
                    "Localization",
                    "Tables",
                    LocalizationSubdirectory));
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            candidates.Add(Path.Combine(
                projectRoot,
                "Packages",
                "jp.aramaa.ochibi-chans-converter-tool",
                "Editor",
                "Localization",
                "Tables",
                LocalizationSubdirectory));

            candidates.Add(Path.Combine(
                Application.dataPath,
                "Aramaa",
                "OchibiChansConverterTool",
                "Editor",
                "Localization",
                "Tables",
                LocalizationSubdirectory));

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static string GetSystemLanguageCode()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese:
                    return LanguageJapanese;
                case SystemLanguage.ChineseSimplified:
                    return LanguageChineseSimplified;
                case SystemLanguage.ChineseTraditional:
                    return LanguageChineseTraditional;
                case SystemLanguage.Korean:
                    return LanguageKorean;
                default:
                    return LanguageEnglish;
            }
        }

        private static string NormalizeLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                return LanguageEnglish;
            }

            if (string.Equals(languageCode, LanguageJapanese, StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "ja", StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "ja-JP", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageJapanese;
            }

            if (string.Equals(languageCode, LanguageChineseSimplified, StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageChineseSimplified;
            }

            if (string.Equals(languageCode, LanguageChineseTraditional, StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "zh-TW", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageChineseTraditional;
            }

            if (string.Equals(languageCode, LanguageKorean, StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "ko", StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "ko-KR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(languageCode, "ko_KR", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageKorean;
            }

            return LanguageEnglish;
        }

        [Serializable]
        private sealed class LocalizationData
        {
            public string language;
            public LocalizationEntry[] entries;
        }

        [Serializable]
        private sealed class LocalizationEntry
        {
            public string key;
            public string value;
        }
    }
}
#endif
