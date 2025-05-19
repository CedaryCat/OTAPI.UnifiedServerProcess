using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework {
    public interface IArgumentBuildProcessor<TSource>
        where TSource : Argument {
        public void Apply(LoggedComponent logger, ref TSource source);
    }
}
