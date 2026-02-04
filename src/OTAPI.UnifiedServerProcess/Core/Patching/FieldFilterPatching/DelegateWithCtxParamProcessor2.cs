using ModFramework;
using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class DelegateWithCtxParamProcessor2(TypeDefinition rootContextDef) : IFieldFilterArgProcessor, IJumpSitesCacheFeature
    {
        public string Name => nameof(DelegateWithCtxParamProcessor);

        public FieldReference[] FieldTasks() {
            var module = rootContextDef.Module;
            return [
                module.GetType("Terraria.DataStructures.PlacementHook").Field("hook"),
                module.GetType("Terraria.WorldBuilding.AWorldGenerationOption").Field("OnOptionStateChanged"),
            ];
        }

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var field in FieldTasks()) {
            }
        }

        public void RunTask(ModuleDefinition module, FieldDefinition field) {

        }
    }
}
