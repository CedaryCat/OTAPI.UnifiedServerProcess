using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    /// <summary>
    /// It's probably about removing some useless code logic to avoid unnecessary localization.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class RemoveUnusedCodePatcherAtBegin(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(RemoveUnusedCodePatcherAtBegin);

        public override void Patch() {
            ClearMethodBody(module.GetType("Terraria.Graphics.FinalFractalHelper/FinalFractalProfile").Method("StripDust"));

            foreach (var method in module.GetType("Terraria.DelegateMethods/Minecart").Methods) {
                if (method.IsConstructor || method.ReturnType.FullName != module.TypeSystem.Void.FullName) {
                    continue;
                }
                ClearMethodBody(method);
            }

            var mountTypeDef = module.GetType("Terraria.Mount");
            ClearMethodBody(mountTypeDef.Method("MeowcartLandingSound"));
            ClearMethodBody(mountTypeDef.Method("MeowcartBumperSound"));
        }

        private static void ClearMethodBody(MethodDefinition method) {
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
    }
}
