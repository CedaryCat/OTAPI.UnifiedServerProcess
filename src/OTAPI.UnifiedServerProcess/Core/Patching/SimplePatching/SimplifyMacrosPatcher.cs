using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching {
    /// <summary>
    /// Ensure that all logical branches use the long-form, so that any new IL insertion will not cause an overflow in the calculation of the distance between instructions.
    /// <para>However, since the code extensively uses a large number of judgments related to other short-form instructions (e.g., ldarg.0, ldloc.0), we will not handle the others.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class SimplifyMacrosPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger) {
        public override string Name => nameof(SimplifyMacrosPatcher);

        public override void Patch() {
            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    // method.Body.SimplifyMacros();

                    foreach (var instruction in method.Body.Instructions) {
                        switch (instruction.OpCode.Code) {
                            case Code.Br_S:
                                instruction.OpCode = OpCodes.Br;
                                break;
                            case Code.Brfalse_S:
                                instruction.OpCode = OpCodes.Brfalse;
                                break;
                            case Code.Brtrue_S:
                                instruction.OpCode = OpCodes.Brtrue;
                                break;
                            case Code.Beq_S:
                                instruction.OpCode = OpCodes.Beq;
                                break;
                            case Code.Bge_S:
                                instruction.OpCode = OpCodes.Bge;
                                break;
                            case Code.Bgt_S:
                                instruction.OpCode = OpCodes.Bgt;
                                break;
                            case Code.Ble_S:
                                instruction.OpCode = OpCodes.Ble;
                                break;
                            case Code.Blt_S:
                                instruction.OpCode = OpCodes.Blt;
                                break;
                            case Code.Bne_Un_S:
                                instruction.OpCode = OpCodes.Bne_Un;
                                break;
                            case Code.Bge_Un_S:
                                instruction.OpCode = OpCodes.Bge_Un;
                                break;
                            case Code.Bgt_Un_S:
                                instruction.OpCode = OpCodes.Bgt_Un;
                                break;
                            case Code.Ble_Un_S:
                                instruction.OpCode = OpCodes.Ble_Un;
                                break;
                            case Code.Blt_Un_S:
                                instruction.OpCode = OpCodes.Blt_Un;
                                break;
                            case Code.Leave_S:
                                instruction.OpCode = OpCodes.Leave;
                                break;
                        }
                    }
                }
            }
        }
    }
}
