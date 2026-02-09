using System;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements
{
    public class FileLogger : Logger, IDisposable
    {
        private readonly string filePath;
        private readonly FileStream fileStream;
        private readonly StreamWriter writer;
        private readonly Channel<string> logChannel;
        private readonly Task processingTask;

        public FileLogger(string filePrefix = "Log", string? folder = null) {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            filePath = folder is not null
                ? Path.Combine(folder, $"{filePrefix}_{timestamp}.log")
                : $"{filePrefix}_{timestamp}.log";

            fileStream = File.Create(filePath);
            writer = new StreamWriter(fileStream);

            logChannel = Channel.CreateUnbounded<string>();

            processingTask = Task.Factory.StartNew(
                ProcessLogEntries,
                TaskCreationOptions.LongRunning
            ).Unwrap();
        }

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            string line = FormatLogLine(sender, level, segments, false);
            logChannel.Writer.TryWrite(line);
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            string line = FormatLogLine(sender, level, segments, true);
            logChannel.Writer.TryWrite(line);
        }

        private static string FormatLogLine(
            ILoggedComponent sender,
            int level,
            ReadOnlyMemory<ColoredSegment> segments,
            bool withNewLine
        ) {
            string levelName = GetLevelName(level);
            string text = string.Join("", segments.ToArray().Select(s => s.Text));
            return $"[{levelName}] {text}{(withNewLine ? Environment.NewLine : "")}";
        }

        private async Task ProcessLogEntries() {
            try {
                await foreach (string logEntry in logChannel.Reader.ReadAllAsync()) {
                    try {
                        writer.WriteLine(logEntry);
                        await writer.FlushAsync();
                    }
                    catch (Exception writeEx) {
                        Console.WriteLine($"Log write failed: {writeEx.Message}");
                    }
                }
            }
            finally {
                await writer.FlushAsync();
            }
        }

        private static string GetLevelName(int level) => level switch {
            DEBUG => "DEBUG",
            PROGRESS => "PROGRESS",
            INFO => "INFO",
            WARN => "WARN",
            ERROR => "ERROR",
            FATAL => "FATAL",
            _ => "UNKNOWN"
        };

        public void Dispose() {
            logChannel.Writer.Complete();

            try {
                processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ae) {
                ae.Handle(ex => ex is TaskCanceledException);
            }
            writer.Dispose();
            fileStream.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
