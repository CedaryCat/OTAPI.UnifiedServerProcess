using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Loggers.Implements {
    public class CompositeLogger(params Logger[] loggers) : Logger, IDisposable {

        public void Dispose() {
            foreach (var logger in loggers.OfType<IDisposable>()) {
                logger.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        public override void LogSegments(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            foreach (var logger in loggers) {
                logger.LogSegments(sender, level, segments);
            }
        }

        public override void LogSegmentsLine(ILoggedComponent sender, int level, ReadOnlyMemory<ColoredSegment> segments) {
            foreach (var logger in loggers) {
                logger.LogSegmentsLine(sender, level, segments);
            }
        }
    }
}
