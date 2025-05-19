using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements {

    public class ConsoleLogger : Logger, IDisposable {
        private readonly int _minLevel;
        private readonly Channel<LogMessage> _channel;
        private readonly Task _processingTask;

        private readonly record struct LogMessage(
            int Level,
            ReadOnlyMemory<ColoredSegment> Segments,
            bool IsLine
        );

        public ConsoleLogger(int minLevel = DEBUG) : base() {
            _minLevel = minLevel;
            _channel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(1000) {
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _processingTask = Task.Run(ProcessLogMessagesAsync);
        }

        private async Task ProcessLogMessagesAsync() {
            await foreach (var message in _channel.Reader.ReadAllAsync()) {
                var originalFg = Console.ForegroundColor;
                var originalBg = Console.BackgroundColor;

                try {
                    foreach (var segment in message.Segments.Span) {
                        Console.ForegroundColor = segment.ForegroundColor;
                        Console.BackgroundColor = segment.BackgroundColor;
                        Console.Write(segment.Text);
                    }

                    if (message.IsLine) Console.WriteLine();
                }
                finally {
                    Console.ForegroundColor = originalFg;
                    Console.BackgroundColor = originalBg;
                }
            }
        }

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            if (level < _minLevel) return;
            _channel.Writer.TryWrite(new LogMessage(level, segments, false));
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            if (level < _minLevel) return;
            _channel.Writer.TryWrite(new LogMessage(level, segments, true));
        }

        public void Dispose() {
            _channel.Writer.Complete();
            _processingTask.GetAwaiter().GetResult();

            GC.SuppressFinalize(this);
        }
    }
}
