using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class ServerNetmodeProcessor : IFieldFilterArgProcessor, IJumpSitesCacheFeature
    {
        public string Name => nameof(ServerNetmodeProcessor);

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {

            TypeDefinition mainTypeDef = source.MainModule.GetType("Terraria.Main");
            FieldDefinition netMode = mainTypeDef.GetField("netMode");
            FieldDefinition myPlayer = mainTypeDef.GetField("myPlayer");
            FieldDefinition hasPendingNetmodeChange = mainTypeDef.GetField("_hasPendingNetmodeChange");
            MethodDefinition doUpdateMethodDef = mainTypeDef.GetMethod("mfwh_DoUpdate");
            MethodDefinition cctorMethodDef = mainTypeDef.GetMethod(".cctor");

            Instruction[] inserts = [
                Instruction.Create(OpCodes.Ldc_I4_2),
                Instruction.Create(OpCodes.Stsfld, netMode),
                Instruction.Create(OpCodes.Ldc_I4, (int)byte.MaxValue),
                Instruction.Create(OpCodes.Stsfld, myPlayer)
            ];

            foreach (Instruction? inst in inserts.Reverse()) {
                cctorMethodDef.Body.Instructions.Insert(0, inst);
            }

            source.ModifiedStaticFields.Remove(netMode.GetIdentifier());
            source.ModifiedStaticFields.Remove(hasPendingNetmodeChange.GetIdentifier());
            source.ModifiedStaticFields.Remove(myPlayer.GetIdentifier());

            Dictionary<Instruction, List<Instruction>> doUpdateMethodJumpSites = this.GetMethodJumpSites(doUpdateMethodDef);

            bool removing = false;
            for (int i = 0; i < doUpdateMethodDef.Body.Instructions.Count; i++) {
                Instruction inst = doUpdateMethodDef.Body.Instructions[i];
                if (inst is { OpCode.Code: Code.Ldsfld, Operand: FieldReference { Name: "_hasPendingNetmodeChange" } }) {
                    removing = true;
                }

                Instruction check = inst;

                if (removing) {
                    if (doUpdateMethodJumpSites.ContainsKey(inst)) {
                        check = inst.Clone();

                        inst.OpCode = OpCodes.Nop;
                        inst.Operand = null;
                    }
                    else {
                        doUpdateMethodDef.Body.Instructions.Remove(inst);
                        i--;
                    }
                }

                if (check is { OpCode.Code: Code.Stsfld, Operand: FieldReference { Name: "_hasPendingNetmodeChange" } }) {
                    break;
                }
            }

            foreach (MethodDefinition? method in mainTypeDef.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (Instruction? inst in method.Body.Instructions) {
                    if (inst is not { OpCode.Code: Code.Stsfld, Operand: FieldReference { Name: "netMode" } }) {
                        continue;
                    }
                    MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource>[] paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, inst, this.GetMethodJumpSites(method));
                    Instruction ldc = paths.Single()
                        .ParametersSources.Single()
                        .Instructions.Single();

                    ldc.OpCode = OpCodes.Ldc_I4_2;
                }
            }
        }
    }
}
