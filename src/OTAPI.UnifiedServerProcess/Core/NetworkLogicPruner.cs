using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core
{
    public class NetworkLogicPruner(ModuleDefinition module)
    {
        readonly FieldDefinition dedServ = module.GetType("Terraria.Main").Field("dedServ");
        readonly FieldDefinition skipMenu = module.GetType("Terraria.Main").Field("skipMenu");

        // TODO: support more cases
        // readonly FieldDefinition netMode = module.GetType("Terraria.Main").Field("netMode");

        public void Prune() {

            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }

                    Dictionary<Instruction, Instruction> switchBlockEnd = [];

                    bool goingOn = false;
                    foreach (var inst in method.Body.Instructions) {
                        if (inst.Operand is not FieldReference fieldReference) {
                            continue;
                        }
                        if (fieldReference.FullName == dedServ.FullName || fieldReference.FullName == skipMenu.FullName) {
                            goingOn = true;
                            break;
                        }
                    }

                    if (!goingOn) {
                        continue;
                    }

                    foreach (var inst in method.Body.Instructions) {
                        if (inst.OpCode != OpCodes.Switch) {
                            continue;
                        }
                        var targets = ((Instruction[])inst.Operand).ToHashSet();
                        var spliters = targets.ToHashSet();
                        if (inst.Next.OpCode != OpCodes.Ret && inst.Next.OpCode != OpCodes.Br && inst.Next.OpCode != OpCodes.Br_S) {
                            targets.Add(inst.Next);
                        }

                        Instruction? switchEnd = inst;
                        while (switchEnd.OpCode != OpCodes.Br && switchEnd.OpCode != OpCodes.Br_S && switchEnd.OpCode != OpCodes.Ret) {
                            switchEnd = switchEnd.Next;
                        }

                        if (switchEnd.Operand is null) {
                            spliters.Add(switchEnd.Next);
                        }
                        else {
                            switchEnd = (Instruction)switchEnd.Operand;
                            spliters.Add(switchEnd);
                            switchEnd = switchEnd.Previous;
                        }

                        foreach (var target in targets) {
                            var checking = target.Next;
                            var blockEnd = checking;
                            HashSet<Instruction> block = [target];

                            if (checking is null) {
                                switchBlockEnd[target] = target;
                                continue;
                            }

                            while (!spliters.Contains(checking)) {
                                block.Add(checking);
                                blockEnd = checking;
                                if (checking.Next is null) {
                                    break;
                                }
                                checking = checking.Next;
                            }

                            if (blockEnd.OpCode != OpCodes.Ret
                                && blockEnd.OpCode != OpCodes.Br
                                && blockEnd.OpCode != OpCodes.Br_S
                                && blockEnd != switchEnd) {

                                if (blockEnd.Next == switchEnd || blockEnd.Next.OpCode == OpCodes.Ret) {
                                    block.Add(blockEnd);
                                    blockEnd = switchEnd;
                                }
                                else {

                                }
                            }

                            block.Add(blockEnd);
                            foreach (var inst2 in block) {
                                switchBlockEnd[inst2] = blockEnd;
                            }
                        }
                    }

                    List<Instruction> reachableInstructions = new List<Instruction>(method.Body.Instructions.Count);
                    List<Instruction> removes = new List<Instruction>();

                    var jumpSites = MonoModCommon.Stack.BuildJumpSitesMap(method);

                    Dictionary<Instruction, int> indexMap = [];
                    for (int i = 0; i < method.Body.Instructions.Count; i++) {
                        indexMap[method.Body.Instructions[i]] = i;
                    }

                    var data = (-1, -1);

                    for (int i = 0; i < method.Body.Instructions.Count;) {
                        var reachableInst = method.Body.Instructions[i];
                        CanReachCurrentInstruction(method.Body, jumpSites, switchBlockEnd, reachableInst, removes, indexMap, ref i, ref data);
                        reachableInstructions.Add(reachableInst);
                    }

                    foreach (var rm in removes) {
                        method.Body.RemoveInstructionSeamlessly(jumpSites, rm, reachableInstructions);
                    }

                    method.Body.Instructions.Clear();
                    method.Body.Instructions.AddRange(reachableInstructions);
                }
            }
        }

        void CanReachCurrentInstruction(

            MethodBody body,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            Dictionary<Instruction, Instruction> switchBlockToEnd,
            Instruction instruction,
            List<Instruction> rm,
            Dictionary<Instruction, int> indexMap,

            ref int index, ref (int end_dedServIsTrueBlock, int end_skipMenuIsFalseBlock) data) {


            if (index > data.end_dedServIsTrueBlock) {
                data.end_dedServIsTrueBlock = -1;
            }

            if (index > data.end_skipMenuIsFalseBlock) {
                data.end_skipMenuIsFalseBlock = -1;
            }

            if (instruction.MatchLdsfld(out var fieldReference)) {
                if (fieldReference.FullName == dedServ.FullName) {
                    var nextInst = instruction.Next;

                    if (nextInst.OpCode == OpCodes.Brtrue || nextInst.OpCode == OpCodes.Brtrue_S) {
                        var jumpTarget = (Instruction)nextInst.Operand;

                        var jumpIndex = indexMap[jumpTarget];
                        index = jumpIndex;

                        if (switchBlockToEnd.TryGetValue(instruction, out var blockEnd)) {
                            if (blockEnd.OpCode != OpCodes.Ret
                                && blockEnd.OpCode != OpCodes.Br
                                && blockEnd.OpCode != OpCodes.Br_S) {
                                blockEnd = blockEnd.Next;
                            }
                            var switchEndIndex = indexMap[blockEnd];
                            if (switchEndIndex < jumpIndex) {
                                index = switchEndIndex;
                            }
                            return;
                        }

                        // else block
                        if (instruction.Previous is not null && (instruction.Previous.OpCode == OpCodes.Br || instruction.Previous.OpCode == OpCodes.Br_S)) {
                            jumpTarget = (Instruction)instruction.Previous.Operand;
                            jumpIndex = indexMap[jumpTarget];
                            data.end_dedServIsTrueBlock = jumpIndex;
                        }

                        rm.Add(instruction);
                        return;
                    }

                    if (nextInst.OpCode == OpCodes.Brfalse || nextInst.OpCode == OpCodes.Brfalse_S) {
                        var jumpTarget = (Instruction)nextInst.Operand;
                        if (jumpSites[jumpTarget].Count == 1) {
                            data.end_dedServIsTrueBlock = indexMap[jumpTarget];
                            rm.Add(instruction);
                            rm.Add(nextInst);
                        }
                    }
                }
                if (fieldReference.FullName == skipMenu.FullName) {
                    var nextInst = instruction.Next;

                    if (nextInst.OpCode == OpCodes.Brfalse || nextInst.OpCode == OpCodes.Brfalse_S) {
                        var jumpTarget = (Instruction)nextInst.Operand;

                        var jumpIndex = indexMap[jumpTarget];
                        index = jumpIndex;

                        if (switchBlockToEnd.TryGetValue(instruction, out var blockEnd)) {
                            if (blockEnd.OpCode != OpCodes.Ret
                                && blockEnd.OpCode != OpCodes.Br
                                && blockEnd.OpCode != OpCodes.Br_S) {
                                blockEnd = blockEnd.Next;
                            }
                            var switchEndIndex = indexMap[blockEnd];
                            if (switchEndIndex < jumpIndex) {
                                index = switchEndIndex;
                            }
                            return;
                        }

                        // else block
                        if (instruction.Previous is not null && (instruction.Previous.OpCode == OpCodes.Br || instruction.Previous.OpCode == OpCodes.Br_S)) {
                            jumpTarget = (Instruction)instruction.Previous.Operand;
                            jumpIndex = indexMap[jumpTarget];
                            data.end_skipMenuIsFalseBlock = jumpIndex;
                        }

                        rm.Add(instruction);
                        return;
                    }

                    if (nextInst.OpCode == OpCodes.Brtrue || nextInst.OpCode == OpCodes.Brtrue_S) {
                        var jumpTarget = (Instruction)nextInst.Operand;
                        if (jumpSites[jumpTarget].Count == 1) {
                            data.end_skipMenuIsFalseBlock = indexMap[jumpTarget];
                            rm.Add(instruction);
                            rm.Add(nextInst);
                        }
                    }
                }
            }

            if (CheckIsJumpOutOfBlock(body, instruction, switchBlockToEnd, rm, indexMap, ref index, data.end_dedServIsTrueBlock)) {
                return;
            }

            if (CheckIsJumpOutOfBlock(body, instruction, switchBlockToEnd, rm, indexMap, ref index, data.end_skipMenuIsFalseBlock)) {
                return;
            }

            index += 1;

            return;

            static bool CheckIsJumpOutOfBlock(MethodBody body, Instruction instruction, Dictionary<Instruction, Instruction> switchBlockToEnd, List<Instruction> rm, Dictionary<Instruction, int> indexMap, ref int index, int endOfBlock) {

                if (endOfBlock >= 0) {

                    if (instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Br_S) {
                        var jumpTarget = (Instruction)instruction.Operand;

                        var jumpIndex = indexMap[jumpTarget];

                        if (jumpIndex > endOfBlock) {
                            index = jumpIndex;

                            rm.Add(instruction);
                            return true;
                        }

                        if (switchBlockToEnd.TryGetValue(instruction, out var blockEnd)) {
                            if (blockEnd.OpCode != OpCodes.Ret
                                && blockEnd.OpCode != OpCodes.Br
                                && blockEnd.OpCode != OpCodes.Br_S) {
                                blockEnd = blockEnd.Next;
                            }
                            var switchEndIndex = indexMap[blockEnd];
                            if (switchEndIndex < index) {
                                index = switchEndIndex;
                            }
                            return true;
                        }
                    }

                    if (instruction.OpCode == OpCodes.Ret) {
                        index = body.Instructions.Count;

                        if (switchBlockToEnd.TryGetValue(instruction, out var blockEnd)) {

                            if (blockEnd.OpCode != OpCodes.Ret
                                && blockEnd.OpCode != OpCodes.Br
                                && blockEnd.OpCode != OpCodes.Br_S) {
                                blockEnd = blockEnd.Next;
                            }

                            if (blockEnd.OpCode == OpCodes.Ret && blockEnd != instruction) {
                                rm.Add(instruction);
                            }

                            var switchEndIndex = indexMap[blockEnd];
                            if (switchEndIndex < index) {
                                index = switchEndIndex + 1;
                            }
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
