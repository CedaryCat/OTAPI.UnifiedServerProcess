using System;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements {
    public class FilteredMarkdownLogger(int minLevel, string filePrefix = "Log_console", string? folder = null) : MarkdownLogger(filePrefix, folder) {
        private readonly int minLevel = minLevel;

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            if (level >= minLevel) base.LogSegments(sender, level, segments);
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            if (level >= minLevel) base.LogSegmentsLine(sender, level, segments);
        }
    }
}
