using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class InitLocalsFixPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(InitLocalsFixPatcher);

        public override void Patch() {
            foreach (var type in module.GetAllTypes()) {
                foreach (var m in type.Methods) {
                    if (m.Body is not null) {
                        m.Body.InitLocals = m.Body.HasVariables;
                    }
                }
            }
        }
    }
}
