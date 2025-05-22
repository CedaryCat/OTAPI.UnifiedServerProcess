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
