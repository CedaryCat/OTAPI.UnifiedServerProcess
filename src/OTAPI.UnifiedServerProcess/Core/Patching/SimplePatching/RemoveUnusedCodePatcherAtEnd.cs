using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class RemoveUnusedCodePatcherAtEnd(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(RemoveUnusedCodePatcherAtEnd);

        public override void Patch() {

            var legarcyLighting = module.GetType("Terraria.Graphics.Light.LegacyLighting");
            var legarcyLighting_ctor = legarcyLighting.Methods.Single(m => m.Name == ".ctor");

            legarcyLighting_ctor.Body.Variables.Clear();
            legarcyLighting_ctor.Body.Instructions.Clear();
            legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            var lightSysCxt = module.GetType("Terraria.LightingSystemContext");
            var lightSysCxt_ctor = lightSysCxt.Methods.Single(m => m.Name == ".ctor");

            lightSysCxt_ctor.Body.Variables.Clear();
            lightSysCxt_ctor.Body.Instructions.Clear();
            lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
    }
}
