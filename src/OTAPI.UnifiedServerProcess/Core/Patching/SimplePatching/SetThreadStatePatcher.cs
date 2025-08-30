using ModFramework;
using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class SetThreadStatePatcher(ILogger logger, ModuleDefinition module, MethodCallGraph callGraph) : Patcher(logger), IMethodCheckCacheFeature
    {
        public MethodCallGraph MethodCallGraph => callGraph;
        public override string Name => nameof(SetThreadStatePatcher);

        public override void Patch() {
            this.ForceOverrideContextBoundCheck(module.GetType("Terraria.Main").Method("mfwh_NeverSleep"), true);
            this.ForceOverrideContextBoundCheck(module.GetType("Terraria.Main").Method("mfwh_YouCanSleepNow"), true);
        }
    }
}
