using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core
{
    public class NetworkLogicPruner(ModuleDefinition module)
    {
        readonly FieldDefinition dedServ = module.GetType("Terraria.Main").GetField("dedServ");

        // TODO: support more cases
        // readonly FieldDefinition netMode = module.GetType("Terraria.Main").Field("netMode");

        /// <summary>
        /// Prunes client-only code paths by assuming Terraria.Main.dedServ is constant true.
        /// This pass is intentionally conservative and keeps the method body's structure intact
        /// (especially exception handlers) by NOP'ing unreachable instructions in place.
        /// </summary>
        public void Prune(params string[] skippedTypeFullNames) {
            var skipTypes = skippedTypeFullNames.ToHashSet();

            foreach (var type in module.GetAllTypes()) {
                if (skipTypes.Contains(type.FullName)) {
                    continue;
                }

                foreach (var method in type.Methods.ToArray()) {
                    if (!method.HasBody) {
                        continue;
                    }

                    var body = method.Body;
                    if (!ReferencesDedServ(body)) {
                        continue;
                    }

                    // The previous implementation rebuilt the instruction list, which is very easy to get wrong
                    // with try/catch/finally/filter blocks. If EH boundaries reference instructions that no longer
                    // exist in body.Instructions, Cecil can fail later (often surfacing around ReadExceptionHandlers).
                    //
                    // This implementation only mutates instructions in-place, so EH boundaries remain stable.

                    body.SimplifyMacros();

                    bool changed = FoldDedServConditionalBranches(body, assumeDedServ: true);
                    changed |= NopUnreachableInstructions(body);

                    if (changed) {
                        body.OptimizeMacros();
                    }
                }
            }
        }

        bool ReferencesDedServ(MethodBody body) {
            foreach (var instruction in body.Instructions) {
                if (instruction.Operand is FieldReference fieldReference
                    && fieldReference.FullName == dedServ.FullName) {
                    return true;
                }
            }
            return false;
        }

        static bool IsConditionalBranch(OpCode opCode)
            => opCode.Code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S;

        static bool IsBrtrue(OpCode opCode) => opCode.Code is Code.Brtrue or Code.Brtrue_S;

        static bool IsShortBranch(OpCode opCode)
            => opCode.Code is Code.Brtrue_S or Code.Brfalse_S or Code.Br_S;

        static bool TryResolveBranchTarget(object? operand, out Instruction? target) {
            switch (operand) {
                case Instruction instruction:
                    target = instruction;
                    return true;
                case ILLabel label:
                    target = label.Target;
                    return target is not null;
                default:
                    target = null;
                    return false;
            }
        }

        static bool TryResolveSwitchTargets(object? operand, out IReadOnlyList<Instruction> targets) {
            if (operand is Instruction[] insts) {
                targets = insts;
                return true;
            }

            if (operand is ILLabel[] labels) {
                var resolved = new List<Instruction>(labels.Length);
                foreach (var label in labels) {
                    if (label.Target is not null) {
                        resolved.Add(label.Target);
                    }
                }
                targets = resolved;
                return true;
            }

            targets = [];
            return false;
        }

        bool FoldDedServConditionalBranches(MethodBody body, bool assumeDedServ) {
            bool changed = false;
            var instructions = body.Instructions;

            for (int i = 0; i < instructions.Count - 1; i++) {
                var load = instructions[i];
                if (load.OpCode.Code != Code.Ldsfld) {
                    continue;
                }

                if (load.Operand is not FieldReference fieldReference || fieldReference.FullName != dedServ.FullName) {
                    continue;
                }

                var branch = instructions[i + 1];
                if (!IsConditionalBranch(branch.OpCode)) {
                    continue;
                }

                // The conditional branch consumes the value produced by ldsfld. Replace both instructions so the stack stays valid.
                bool takeBranch = IsBrtrue(branch.OpCode) ? assumeDedServ : !assumeDedServ;

                load.OpCode = OpCodes.Nop;
                load.Operand = null;

                if (takeBranch) {
                    branch.OpCode = IsShortBranch(branch.OpCode) ? OpCodes.Br_S : OpCodes.Br;
                }
                else {
                    branch.OpCode = OpCodes.Nop;
                    branch.Operand = null;
                }

                changed = true;
                i += 1; // Skip the branch we just handled.
            }

            return changed;
        }

        bool NopUnreachableInstructions(MethodBody body) {
            var instructions = body.Instructions;
            if (instructions.Count == 0) {
                return false;
            }

            var indexMap = new Dictionary<Instruction, int>(instructions.Count);
            for (int i = 0; i < instructions.Count; i++) {
                indexMap[instructions[i]] = i;
            }

            // Conservative EH edge: if any instruction in a try region is reachable, consider its handler/filter reachable too.
            // This is important for finally blocks (executed via "hidden" control-flow during leave/unwind).
            List<(int tryStart, int tryEnd, int handlerStart, int filterStart)> exceptionHandlers = [];
            if (body.HasExceptionHandlers) {
                foreach (var handler in body.ExceptionHandlers) {
                    if (handler.TryStart is null || !indexMap.TryGetValue(handler.TryStart, out var tryStart)) {
                        continue;
                    }

                    int tryEnd;
                    if (handler.TryEnd is null) {
                        tryEnd = instructions.Count;
                    }
                    else if (!indexMap.TryGetValue(handler.TryEnd, out tryEnd)) {
                        // Invalid boundary (likely already broken). Avoid making things worse by skipping EH-driven edges.
                        continue;
                    }

                    int handlerStart = -1;
                    if (handler.HandlerStart is not null && indexMap.TryGetValue(handler.HandlerStart, out var hs)) {
                        handlerStart = hs;
                    }

                    int filterStart = -1;
                    if (handler.FilterStart is not null && indexMap.TryGetValue(handler.FilterStart, out var fs)) {
                        filterStart = fs;
                    }

                    exceptionHandlers.Add((tryStart, tryEnd, handlerStart, filterStart));
                }
            }

            var reachable = new bool[instructions.Count];
            var work = new Stack<int>();

            reachable[0] = true;
            work.Push(0);

            while (work.TryPop(out var currentIndex)) {
                var current = instructions[currentIndex];

                foreach (var (tryStart, tryEnd, handlerStart, filterStart) in exceptionHandlers) {
                    if (currentIndex < tryStart || currentIndex >= tryEnd) {
                        continue;
                    }

                    if (filterStart >= 0 && !reachable[filterStart]) {
                        reachable[filterStart] = true;
                        work.Push(filterStart);
                    }

                    if (handlerStart >= 0 && !reachable[handlerStart]) {
                        reachable[handlerStart] = true;
                        work.Push(handlerStart);
                    }
                }

                if (current.OpCode.Code == Code.Switch) {
                    if (TryResolveSwitchTargets(current.Operand, out var targets)) {
                        foreach (var target in targets) {
                            if (indexMap.TryGetValue(target, out var targetIndex) && !reachable[targetIndex]) {
                                reachable[targetIndex] = true;
                                work.Push(targetIndex);
                            }
                        }
                    }

                    var fallthrough = currentIndex + 1;
                    if (fallthrough < instructions.Count && !reachable[fallthrough]) {
                        reachable[fallthrough] = true;
                        work.Push(fallthrough);
                    }

                    continue;
                }

                switch (current.OpCode.FlowControl) {
                    case FlowControl.Branch: {
                            if (TryResolveBranchTarget(current.Operand, out var target)
                                && target is not null
                                && indexMap.TryGetValue(target, out var targetIndex)
                                && !reachable[targetIndex]) {
                                reachable[targetIndex] = true;
                                work.Push(targetIndex);
                            }
                            break;
                        }

                    case FlowControl.Cond_Branch: {
                            if (TryResolveBranchTarget(current.Operand, out var target)
                                && target is not null
                                && indexMap.TryGetValue(target, out var targetIndex)
                                && !reachable[targetIndex]) {
                                reachable[targetIndex] = true;
                                work.Push(targetIndex);
                            }

                            var fallthrough = currentIndex + 1;
                            if (fallthrough < instructions.Count && !reachable[fallthrough]) {
                                reachable[fallthrough] = true;
                                work.Push(fallthrough);
                            }
                            break;
                        }

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        break;

                    default: {
                            var next = currentIndex + 1;
                            if (next < instructions.Count && !reachable[next]) {
                                reachable[next] = true;
                                work.Push(next);
                            }
                            break;
                        }
                }
            }

            bool changed = false;
            for (int i = 0; i < instructions.Count; i++) {
                if (reachable[i]) {
                    continue;
                }

                var instruction = instructions[i];
                if (instruction.OpCode.Code == Code.Nop && instruction.Operand is null) {
                    continue;
                }

                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;
                changed = true;
            }

            return changed;
        }
    }
}
