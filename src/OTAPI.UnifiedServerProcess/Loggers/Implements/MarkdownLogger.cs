using System;
using System.IO;
using System.Text;
using System.Threading;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements
{
    [Obsolete("Not support")]
    public class MarkdownLogger : Logger, IDisposable
    {
        private readonly string filePath;
        private static readonly Lock mdLock = new();

        public MarkdownLogger(string filePrefix = "Log", string? folder = null) {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (folder is not null) {
                filePath = Path.Combine(folder, $"{filePrefix}_{timestamp}.md");
            }
            else {
                filePath = $"{filePrefix}_{timestamp}.md";
            }
        }

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            lock (mdLock) {
                File.AppendAllText(filePath, ProcessSegments(segments));
            }
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            lock (mdLock) {
                File.AppendAllText(filePath, ProcessSegments(segments) + "\n");
            }
        }

        private static string ProcessSegments(ReadOnlyMemory<ColoredSegment> segments) {
            var sb = new StringBuilder();
            foreach (var segment in segments.Span) {
                string fgColor = GetHexColor(segment.ForegroundColor);
                string bgColor = $"background-color:{GetHexColor(segment.BackgroundColor)}";

                sb.Append($"<span style=\"color:{fgColor};{bgColor}\">");
                sb.Append(segment.Text.Replace("<", "&lt;").Replace(">", "&gt;"));
                sb.Append("</span>");
            }
            return sb.ToString();
        }
        private static string GetHexColor(ConsoleColor color) => color switch {
            ConsoleColor.Black => "#000000",
            ConsoleColor.DarkBlue => "#00008B",
            ConsoleColor.DarkGreen => "#006400",
            ConsoleColor.DarkCyan => "#008B8B",
            ConsoleColor.DarkRed => "#8B0000",
            ConsoleColor.DarkMagenta => "#8B008B",
            ConsoleColor.DarkYellow => "#808000",
            ConsoleColor.Gray => "#808080",
            ConsoleColor.DarkGray => "#A9A9A9",
            ConsoleColor.Blue => "#0000FF",
            ConsoleColor.Green => "#00FF00",
            ConsoleColor.Cyan => "#00FFFF",
            ConsoleColor.Red => "#FF0000",
            ConsoleColor.Magenta => "#FF00FF",
            ConsoleColor.Yellow => "#FFFF00",
            ConsoleColor.White => "#FFFFFF",
            _ => "#000000"
        };

        public void Dispose() {
            File.AppendAllText(filePath, "\n```");
            GC.SuppressFinalize(this);
        }
    }
}
