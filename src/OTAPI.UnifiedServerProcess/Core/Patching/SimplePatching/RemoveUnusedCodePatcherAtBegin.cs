using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    /// <summary>
    /// It's probably about removing some useless code logic to avoid unnecessary localization.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class RemoveUnusedCodePatcherAtBegin(ILogger logger, ModuleDefinition module, MethodCallGraph methodCallGraph) : Patcher(logger), IMethodCheckCacheFeature
    {
        public override string Name => nameof(RemoveUnusedCodePatcherAtBegin);
        public MethodCallGraph MethodCallGraph => methodCallGraph;

        public override void Patch() {
            foreach (var method in module.GetType("Terraria.Initializers.DyeInitializer/<>c").Methods) {
                if (method.Name.StartsWith("<LoadLegacyHairdyes>")) {
                    ClearMethodBody(method);
                }
            }
            ClearMethodBody(module.GetType("Terraria.Lang").GetMethod("InitGlobalSubstitutions"));
            ClearMethodBody(module.GetType("Terraria.Graphics.FinalFractalHelper/FinalFractalProfile").GetMethod("StripDust"));
            ClearMethodBody(module.GetType("Microsoft.Xna.Framework.GameWindow").GetMethod("get_Title"));

            foreach (var method in module.GetType("Terraria.DelegateMethods/Minecart").Methods) {
                if (method.IsConstructor || method.ReturnType.FullName != module.TypeSystem.Void.FullName) {
                    continue;
                }
                ClearMethodBody(method);
            }

            var mountTypeDef = module.GetType("Terraria.Mount");
            ClearMethodBody(mountTypeDef.GetMethod("MeowcartLandingSound"));
            ClearMethodBody(mountTypeDef.GetMethod("MeowcartBumperSound"));

            ClearExHandler(module.GetType("Terraria.Localization.LanguageManager").GetMethod("LoadFromContentSources")); 
        }

        private void ClearMethodBody(MethodDefinition method) {
            this.ForceOverrideContextBoundCheck(method, false);

            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            if (method.ReturnType.FullName != method.Module.TypeSystem.Void.FullName) {
                if (method.ReturnType.IsValueType) {
                    method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, new MethodReference(".ctor", method.Module.TypeSystem.Void, method.ReturnType)));
                }
                else if (method.ReturnType.FullName == module.TypeSystem.String.FullName) {
                    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ""));
                }
                else {
                    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
                }
            }
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        private static void ClearExHandler(MethodDefinition method) {
            if (method?.HasBody != true) return;

            var body = method.Body;
            if (!body.HasExceptionHandlers) return;

            body.SimplifyMacros();
            var il = body.GetILProcessor();

            static IEnumerable<Instruction> Range(Instruction start, Instruction endExclusive) {
                for (var i = start; i != null && i != endExclusive; i = i.Next)
                    yield return i;
            }

            foreach (var eh in body.ExceptionHandlers.ToArray()) {
                if (eh.HandlerType != ExceptionHandlerType.Catch) continue;
                if (eh.HandlerStart == null) continue;

                if (eh.HandlerEnd == null) {
                    var endNop = il.Create(OpCodes.Nop);
                    body.Instructions.Add(endNop);
                    eh.HandlerEnd = endNop;
                }

                var region = Range(eh.HandlerStart, eh.HandlerEnd).ToList();
                if (region.Count == 0) continue;

                var first = region[0];

                bool consumesExceptionObject =
                    first.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3
                    || first.OpCode.Code is Code.Pop;

                if (!consumesExceptionObject) {
                    first.OpCode = OpCodes.Pop;
                    first.Operand = null;
                }
                for (int i = 1; i < region.Count; i++) {
                    region[i].OpCode = OpCodes.Nop;
                    region[i].Operand = null;
                }
                if (region.Count == 1) {
                    il.InsertAfter(first, il.Create(OpCodes.Leave, eh.HandlerEnd));
                }
                else {
                    var last = region[^1];
                    last.OpCode = OpCodes.Leave;
                    last.Operand = eh.HandlerEnd;
                }
            }

            body.OptimizeMacros();
        }
    }
}
