using System;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Loggers
{

    public abstract class Logger : ILogger
    {
        public const int DEBUG = 0;
        public const int PROGRESS = 1;
        public const int INFO = 2;
        public const int WARN = 3;
        public const int ERROR = 4;
        public const int FATAL = 5;

        public abstract void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments);
        public abstract void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments);
        public void Log(ILoggedComponent sender, int level, ConsoleColor prefixColor, params ColoredSegment[] segments) {
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, prefixColor),
                new ColoredSegment("]", ConsoleColor.Cyan),
                .. segments
                ];
            LogSegments(sender, level, content);
        }

        public void LogLine(ILoggedComponent sender, int level, ConsoleColor prefixColor, params ColoredSegment[] segments) {
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, prefixColor),
                new ColoredSegment("]", ConsoleColor.Cyan),
                ..segments
                ];
            LogSegmentsLine(sender, level, content);
        }
        public static ColoredSegment[] FormatString(int indent, string log, params object[] args) {
            log = new string(' ', 1 + indent * 2) + log;

            if (args is null || args.Length == 0) {
                return [new ColoredSegment(log, ConsoleColor.Gray)];
            }

            var segments = new List<ColoredSegment>();
            var parts = log.Split(["{", "}"], StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++) {
                if (i % 2 == 0) {
                    // Regular text part (not an argument)
                    if (!string.IsNullOrEmpty(parts[i])) {
                        segments.Add(new ColoredSegment(parts[i], ConsoleColor.Gray));
                    }
                }
                else {
                    // Argument part
                    if (int.TryParse(parts[i], out int argIndex) && argIndex >= 0 && argIndex < args.Length) {
                        string argText = args[argIndex]?.ToString() ?? "null";
                        segments.Add(new ColoredSegment(argText, ConsoleColor.Black, ConsoleColor.DarkGray));
                    }
                    else {
                        // Invalid format, just add it as regular text
                        segments.Add(new ColoredSegment("{" + parts[i] + "}", ConsoleColor.Gray));
                    }
                }
            }

            return [.. segments];
        }
        public void Debug(ILoggedComponent sender, string log, params object[] args) => Debug(sender, 0, log, args);
        public void Info(ILoggedComponent sender, string log, params object[] args) => Info(sender, 0, log, args);
        public void Warn(ILoggedComponent sender, string log, params object[] args) => Warn(sender, 0, log, args);
        public void Error(ILoggedComponent sender, string log, Exception ex, params object[] args) => Error(sender, 0, log, ex, args);
        public void Error(ILoggedComponent sender, string log, params object[] args) => Error(sender, 0, log, args);
        public void Fatal(ILoggedComponent sender, string log, params object[] args) => Fatal(sender, 0, log, args);

        public void Debug(ILoggedComponent sender, int indent, string log, params object[] args)
            => LogLine(sender, DEBUG, ConsoleColor.Gray, FormatString(indent, log, args));

        public void Info(ILoggedComponent sender, int indent, string log, params object[] args)
            => LogLine(sender, INFO, ConsoleColor.Blue, FormatString(indent, log, args));

        public void Warn(ILoggedComponent sender, int indent, string log, params object[] args)
            => LogLine(sender, WARN, ConsoleColor.Yellow, FormatString(indent, log, args));

        public void Error(ILoggedComponent sender, int indent, string log, Exception ex, params object[] args) {
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, ConsoleColor.Red),
                new ColoredSegment("|", ConsoleColor.Cyan),
                new ColoredSegment(ex.GetType().Name, ConsoleColor.DarkRed),
                new ColoredSegment($"]", ConsoleColor.Cyan),
                ..FormatString(indent, log, [ex, .. args]),
                ];
            LogSegmentsLine(sender, ERROR, content);
        }

        public void Error(ILoggedComponent sender, int indent, string log, params object[] args)
            => LogLine(sender, ERROR, ConsoleColor.Red, FormatString(indent, log, args));

        public void Fatal(ILoggedComponent sender, int indent, string log, params object[] args)
            => LogLine(sender, FATAL, ConsoleColor.DarkRed, FormatString(indent, log, args));

        public void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0) {
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, ConsoleColor.Magenta),
                new ColoredSegment("|", ConsoleColor.Cyan),
                new ColoredSegment("iter:", ConsoleColor.Gray),
                new ColoredSegment(iteration.ToString(), ConsoleColor.DarkGray),
                new ColoredSegment(",", ConsoleColor.Cyan),
                new ColoredSegment("progress:", ConsoleColor.Gray),
                new ColoredSegment($"{progress}/{total}", ConsoleColor.DarkGray),
                new ColoredSegment($"]", ConsoleColor.Cyan),
                ..FormatString(indent, message),
            ];
            LogSegmentsLine(sender, PROGRESS, content);
        }

        public void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0) {
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, ConsoleColor.Magenta),
                new ColoredSegment("|", ConsoleColor.Cyan),
                new ColoredSegment("progress:", ConsoleColor.Gray),
                new ColoredSegment($"{progress}/{total}", ConsoleColor.DarkGray),
                new ColoredSegment($"]", ConsoleColor.Cyan),
                ..FormatString(indent, message),
            ];
            LogSegmentsLine(sender, PROGRESS, content);
        }
        public void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0, params object[] args) {
            if (args.Length == 0) {
                Progress(sender, iteration, progress, total, message, indent);
                return;
            }
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, ConsoleColor.Magenta),
                new ColoredSegment("|", ConsoleColor.Cyan),
                new ColoredSegment("iter:", ConsoleColor.Gray),
                new ColoredSegment(iteration.ToString(), ConsoleColor.DarkGray),
                new ColoredSegment(",", ConsoleColor.Cyan),
                new ColoredSegment("progress:", ConsoleColor.Gray),
                new ColoredSegment($"{progress}/{total}", ConsoleColor.DarkGray),
                new ColoredSegment($"]", ConsoleColor.Cyan),
                ..FormatString(indent, message, args),
            ];
            LogSegmentsLine(sender, PROGRESS, content);
        }

        public void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0, params object[] args) {
            if (args.Length == 0) {
                Progress(sender, progress, total, message, indent);
                return;
            }
            ColoredSegment[] content = [
                new ColoredSegment("[", ConsoleColor.Cyan),
                new ColoredSegment(sender.Name, ConsoleColor.Magenta),
                new ColoredSegment("|", ConsoleColor.Cyan),
                new ColoredSegment("progress:", ConsoleColor.Gray),
                new ColoredSegment($"{progress}/{total}", ConsoleColor.DarkGray),
                new ColoredSegment($"]", ConsoleColor.Cyan),
                ..FormatString(indent, message, args),
            ];
            LogSegmentsLine(sender, PROGRESS, content);
        }
    }
}
