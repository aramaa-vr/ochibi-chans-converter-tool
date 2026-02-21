#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;

namespace Aramaa.OchibiChansConverterTool.Editor.Utilities
{
    /// <summary>
    /// 変換ログの組み立てを管理するヘルパーです。
    /// 実装ロジック側からログ整形を分離し、ログ仕様の変更を局所化します。
    /// </summary>
    internal sealed class OchibiChansConverterToolConversionLogger
    {
        private const int MaxLoggedPathEntries = 80;
        private const int MaxLoggedBlendShapeNamesPerRenderer = 80;
        private const int MaxLoggedBlendShapeEntriesPerSmr = 80;

        private readonly List<string> _logs;

        public OchibiChansConverterToolConversionLogger(List<string> logs)
        {
            _logs = logs;
        }

        public bool IsEnabled => _logs != null;

        public void AddRaw(string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            _logs.Add(message ?? string.Empty);
        }

        public void Add(string key, params object[] args)
        {
            if (!IsEnabled)
            {
                return;
            }

            _logs.Add(args != null && args.Length > 0
                ? OchibiChansConverterToolLocalization.Format(key, args)
                : OchibiChansConverterToolLocalization.Get(key));
        }

        public void Blank()
        {
            AddRaw(string.Empty);
        }

        public void AddStep(string stepNo, string title, params string[] details)
        {
            if (!IsEnabled)
            {
                return;
            }

            Add("Log.Step.Header", stepNo, title);

            if (details == null)
            {
                return;
            }

            foreach (var detail in details)
            {
                if (string.IsNullOrWhiteSpace(detail))
                {
                    continue;
                }

                Add("Log.Step.Detail", detail);
            }
        }

        public void AddPathEntries(IEnumerable<string> entries)
        {
            AddPathEntriesWithLimit(entries, MaxLoggedPathEntries);
        }

        public void AddBlendshapeRendererDetail(string rendererPath, List<string> blendShapeNames)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (blendShapeNames == null || blendShapeNames.Count == 0)
            {
                Add("Log.BlendshapeRendererZero", rendererPath);
                return;
            }

            var namesForLog = blendShapeNames;
            if (blendShapeNames.Count > MaxLoggedBlendShapeNamesPerRenderer)
            {
                namesForLog = blendShapeNames.Take(MaxLoggedBlendShapeNamesPerRenderer).ToList();
            }

            Add("Log.BlendshapeRendererDetail", rendererPath, blendShapeNames.Count, string.Join(", ", namesForLog));
            AddListEllipsisIfNeeded(blendShapeNames.Count, namesForLog.Count);
        }

        public void AddBlendshapeEntries(IEnumerable<string> blendShapeNames)
        {
            AddPathEntriesWithLimit(blendShapeNames, MaxLoggedBlendShapeEntriesPerSmr, "Log.BlendshapeEntry");
        }

        private void AddPathEntriesWithLimit(IEnumerable<string> entries, int maxCount, string key = "Log.PathEntry")
        {
            if (!IsEnabled || entries == null)
            {
                return;
            }

            int logged = 0;
            int total = 0;
            foreach (var entry in entries)
            {
                total++;
                if (logged < maxCount)
                {
                    Add(key, entry);
                    logged++;
                }
            }

            var omitted = total - logged;
            if (omitted > 0)
            {
                Add("Log.ListEllipsisMore", omitted);
            }
        }

        public void AddListEllipsisIfNeeded(int totalCount, int loggedCount)
        {
            if (!IsEnabled)
            {
                return;
            }

            var omitted = totalCount - loggedCount;
            if (omitted > 0)
            {
                Add("Log.ListEllipsisMore", omitted);
            }
        }
    }
}
#endif
