using System;

namespace OTAPI.UnifiedServerProcess.Loggers {
    public abstract partial class LoggedComponent(ILogger logger) : ILoggedComponent {
        readonly ILogger _logger = logger;
        public abstract string Name { get; }
        public void Progress(int iteration, int progress, int total, string message, int indent = 0)
            => _logger.Progress(this, iteration, progress, total, message, indent);
        public void Progress(int progress, int total, string message, int indent = 0)
            => _logger.Progress(this, progress, total, message, indent);
        public void Progress(int iteration, int progress, int total, string message, int indent = 0, params object[] args)
            => _logger.Progress(this, iteration, progress, total, message, indent, args);
        public void Progress(int progress, int total, string message, int indent = 0, params object[] args)
            => _logger.Progress(this, progress, total, message, indent, args);

        public void Debug(int indent, string log, params object[] args)
            => _logger.Debug(this, indent, log, args);
        public void Info(int indent, string log, params object[] args)
            => _logger.Info(this, indent, log, args);
        public void Warn(int indent, string log, params object[] args)
            => _logger.Warn(this, indent, log, args);
        public void Error(int indent, string log, Exception ex, params object[] args)
            => _logger.Error(this, indent, log, ex, args);
        public void Error(int indent, string log, params object[] args)
            => _logger.Error(this, indent, log, args);
        public void Fatal(int indent, string log, params object[] args)
            => _logger.Fatal(this, indent, log, args);


        public void Debug(string log, params object[] args)
            => _logger.Debug(this, log, args);
        public void Info(string log, params object[] args)
            => _logger.Info(this, log, args);
        public void Warn(string log, params object[] args)
            => _logger.Warn(this, log, args);
        public void Error(string log, Exception ex, params object[] args)
            => _logger.Error(this, log, ex, args);
        public void Error(string log, params object[] args)
            => _logger.Error(this, log, args);
        public void Fatal(string log, params object[] args)
            => _logger.Fatal(this, log, args);
    }
}
