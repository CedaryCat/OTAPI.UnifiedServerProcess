using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class ServerNetmodeProcessor : IFieldFilterArgProcessor, IJumpSitesCacheFeature
    {
        public string Name => nameof(ServerNetmodeProcessor);

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {

            var mainTypeDef = source.MainModule.GetType("Terraria.Main");
            var netMode = mainTypeDef.GetField("netMode");
            var myPlayer = mainTypeDef.GetField("myPlayer");
            var hasPendingNetmodeChange = mainTypeDef.GetField("_hasPendingNetmodeChange");
            var doUpdateMethodDef = mainTypeDef.GetMethod("mfwh_DoUpdate");
            var cctorMethodDef = mainTypeDef.GetMethod(".cctor");

            Instruction[] inserts = [
                Instruction.Create(OpCodes.Ldc_I4_2),
                Instruction.Create(OpCodes.Stsfld, netMode),
                Instruction.Create(OpCodes.Ldc_I4, (int)byte.MaxValue),
                Instruction.Create(OpCodes.Stsfld, myPlayer)
            ];

            foreach (var inst in inserts.Reverse()) {
                cctorMethodDef.Body.Instructions.Insert(0, inst);
            }

            source.ModifiedStaticFields.Remove(netMode.GetIdentifier());
            source.ModifiedStaticFields.Remove(hasPendingNetmodeChange.GetIdentifier());
            source.ModifiedStaticFields.Remove(myPlayer.GetIdentifier());

            bool removing = false;
            for (int i = 0; i < doUpdateMethodDef.Body.Instructions.Count; i++) {
                var inst = doUpdateMethodDef.Body.Instructions[i];
                if (inst is { OpCode.Code: Code.Ldsfld, Operand: FieldReference { Name: "_hasPendingNetmodeChange" } }) {
                    removing = true;
                }

                if (removing) {
                    doUpdateMethodDef.Body.Instructions.Remove(inst);
                    i--;
                }

                if (inst is { OpCode.Code: Code.Stsfld, Operand: FieldReference { Name: "_hasPendingNetmodeChange" } }) {
                    break;
                }
            }

            foreach (var method in mainTypeDef.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (var inst in method.Body.Instructions) {
                    if (inst is not { OpCode.Code: Code.Stsfld, Operand: FieldReference { Name: "netMode" } }) {
                        continue;
                    }
                    var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, inst, this.GetMethodJumpSites(method));
                    var ldc = paths.Single()
                        .ParametersSources.Single()
                        .Instructions.Single();

                    ldc.OpCode = OpCodes.Ldc_I4_2;
                }
            }
        }
    }
}
