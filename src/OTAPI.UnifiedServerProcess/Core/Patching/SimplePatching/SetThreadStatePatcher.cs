using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class SetThreadStatePatcher(ILogger logger, ModuleDefinition module, MethodCallGraph callGraph) : Patcher(logger), IMethodCheckCacheFeature
    {
        public MethodCallGraph MethodCallGraph => callGraph;
        public override string Name => nameof(SetThreadStatePatcher);

        public override void Patch() {
            this.ForceOverrideContextBoundCheck(module.GetType("Terraria.Main").GetMethod("mfwh_NeverSleep"), true);
            this.ForceOverrideContextBoundCheck(module.GetType("Terraria.Main").GetMethod("mfwh_YouCanSleepNow"), true);
        }
    }
}
