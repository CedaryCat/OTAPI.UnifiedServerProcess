using System;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements {
    public class FileLogger : Logger, IDisposable {
        private readonly string _filePath;
        private readonly FileStream _fileStream;
        private readonly StreamWriter _writer;
        private readonly Channel<string> _logChannel;
        private readonly Task _processingTask;

        public FileLogger(string filePrefix = "Log", string? folder = null) {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _filePath = folder is not null
                ? Path.Combine(folder, $"{filePrefix}_{timestamp}.log")
                : $"{filePrefix}_{timestamp}.log";

            _fileStream = File.Create(_filePath);
            _writer = new StreamWriter(_fileStream);

            // 创建无界Channel（根据需求可改为有界）
            _logChannel = Channel.CreateUnbounded<string>();

            // 启动后台处理任务
            _processingTask = Task.Factory.StartNew(
                ProcessLogEntries,
                TaskCreationOptions.LongRunning
            ).Unwrap();
        }

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            var line = FormatLogLine(sender, level, segments, false);
            _logChannel.Writer.TryWrite(line);
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            var line = FormatLogLine(sender, level, segments, true);
            _logChannel.Writer.TryWrite(line);
        }

        private static string FormatLogLine(
            ILoggedComponent sender,
            int level,
            ReadOnlyMemory<ColoredSegment> segments,
            bool withNewLine
        ) {
            var levelName = GetLevelName(level);
            var text = string.Join("", segments.ToArray().Select(s => s.Text));
            return $"[{levelName}] {text}{(withNewLine ? Environment.NewLine : "")}";
        }

        private async Task ProcessLogEntries() {
            try {
                await foreach (var logEntry in _logChannel.Reader.ReadAllAsync()) {
                    try {
                        _writer.WriteLine(logEntry);
                        await _writer.FlushAsync();
                    }
                    catch (Exception writeEx) {
                        Console.WriteLine($"Log write failed: {writeEx.Message}");
                    }
                }
            }
            finally {
                await _writer.FlushAsync();
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
            _logChannel.Writer.Complete();

            try {
                _processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ae) {
                ae.Handle(ex => ex is TaskCanceledException);
            }
            _writer.Dispose();
            _fileStream.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
