using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    /// <summary>
    /// Re-optimize long-form instructions into short-form
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class OptimizeMacrosPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(SimplifyMacrosPatcher);

        public override void Patch() {
            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    method.Body.OptimizeMacros();
                }
            }
        }
    }
}
