using System;

namespace OTAPI.UnifiedServerProcess.Loggers {
    public readonly struct ColoredSegment {
        public readonly string Text;
        public readonly ConsoleColor ForegroundColor;
        public readonly ConsoleColor BackgroundColor;

        public ColoredSegment(string text, ConsoleColor color, ConsoleColor bgColor = ConsoleColor.Black) {
            Text = text;
            ForegroundColor = color;
            BackgroundColor = bgColor;
        }
    }
}
