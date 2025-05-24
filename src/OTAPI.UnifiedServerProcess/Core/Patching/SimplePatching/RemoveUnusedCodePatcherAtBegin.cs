using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

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

            var finalFractalProfile = module.GetType("Terraria.Graphics.FinalFractalHelper/FinalFractalProfile");
            var stripDust = finalFractalProfile.Methods.Single(m => m.Name == "StripDust");

            stripDust.Body.Variables.Clear();
            stripDust.Body.Instructions.Clear();
            stripDust.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
    }
}
