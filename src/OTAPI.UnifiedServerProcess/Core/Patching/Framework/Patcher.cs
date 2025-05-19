using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework {
    public abstract class Patcher(ILogger logger) : LoggedComponent(logger) {
        public abstract void Patch();
    }
    public abstract class Patcher<TArgument>(ILogger logger) : LoggedComponent(logger) where TArgument : Argument {
        public abstract void Patch(TArgument arguments);
    }
}
