using System;

namespace OTAPI.UnifiedServerProcess.Loggers
{
    public interface ILogger
    {
        void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0);
        void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0);
        void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0, params object[] args);
        void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0, params object[] args);
        void Debug(ILoggedComponent sender, int indent, string log, params object[] args);
        void Info(ILoggedComponent sender, int indent, string log, params object[] args);
        void Warn(ILoggedComponent sender, int indent, string log, params object[] args);
        void Error(ILoggedComponent sender, int indent, string log, Exception ex, params object[] args);
        void Error(ILoggedComponent sender, int indent, string log, params object[] args);
        void Fatal(ILoggedComponent sender, int indent, string log, params object[] args);
        void Debug(ILoggedComponent sender, string log, params object[] args);
        void Info(ILoggedComponent sender, string log, params object[] args);
        void Warn(ILoggedComponent sender, string log, params object[] args);
        void Error(ILoggedComponent sender, string log, Exception ex, params object[] args);
        void Error(ILoggedComponent sender, string log, params object[] args);
        void Fatal(ILoggedComponent sender, string log, params object[] args);
    }
}
