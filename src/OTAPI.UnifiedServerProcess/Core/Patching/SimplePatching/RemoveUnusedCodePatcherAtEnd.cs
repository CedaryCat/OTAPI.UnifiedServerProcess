using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    /// <summary>
    /// Removes unnecessary client only code
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class RemoveUnusedCodePatcherAtEnd(ILogger logger, TypeDefinition rootDef, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(RemoveUnusedCodePatcherAtEnd);

        public override void Patch() {

            //var legarcyLighting = module.GetType("Terraria.Graphics.Light.LegacyLighting");
            //var legarcyLighting_ctor = legarcyLighting.Methods.Single(m => m.Name == ".ctor");

            //legarcyLighting_ctor.Body.Variables.Clear();
            //legarcyLighting_ctor.Body.Instructions.Clear();
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, new FieldReference(Constants.RootContextFieldName, rootDef, legarcyLighting)));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            //var lightSysCxt = module.GetType("Terraria.LightingSystemContext");
            //var lightSysCxt_ctor = lightSysCxt.Methods.Single(m => m.Name == ".ctor");

            //lightSysCxt_ctor.Body.Variables.Clear();
            //lightSysCxt_ctor.Body.Instructions.Clear();
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, new FieldReference(Constants.RootContextFieldName, rootDef, lightSysCxt)));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
    }
}
