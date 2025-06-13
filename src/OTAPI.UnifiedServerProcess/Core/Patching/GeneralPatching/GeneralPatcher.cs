using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    public abstract class GeneralPatcher(ILogger logger) : Patcher<PatcherArguments>(logger)
    {
    }
}
