using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
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
            HashSet<string> skipTypes = skippedTypeFullNames.ToHashSet();

            foreach (TypeDefinition? type in module.GetAllTypes()) {
                if (skipTypes.Contains(type.FullName)) {
                    continue;
                }

                foreach (MethodDefinition? method in type.Methods.ToArray()) {
                    if (!method.HasBody) {
                        continue;
                    }

                    MethodBody body = method.Body;
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
            foreach (Instruction? instruction in body.Instructions) {
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

        static bool IsEhSkeletonInstruction(OpCode opCode)
            => opCode.Code is Code.Leave or Code.Leave_S or Code.Endfinally or Code.Endfilter or Code.Rethrow;

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
                foreach (ILLabel label in labels) {
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
            Collection<Instruction> instructions = body.Instructions;

            for (int i = 0; i < instructions.Count - 1; i++) {
                Instruction load = instructions[i];
                if (load.OpCode.Code != Code.Ldsfld) {
                    continue;
                }

                if (load.Operand is not FieldReference fieldReference || fieldReference.FullName != dedServ.FullName) {
                    continue;
                }

                Instruction branch = instructions[i + 1];
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

        readonly struct ExceptionHandlerInfo(
            ExceptionHandler handler,
            int tryStart,
            int tryEnd,
            int handlerStart,
            int handlerEnd,
            int filterStart,
            int filterEnd)
        {
            public ExceptionHandler Handler { get; } = handler;
            public int TryStart { get; } = tryStart;
            public int TryEnd { get; } = tryEnd;
            public int HandlerStart { get; } = handlerStart;
            public int HandlerEnd { get; } = handlerEnd;
            public int FilterStart { get; } = filterStart;
            public int FilterEnd { get; } = filterEnd;
            public bool HasFilter => FilterStart >= 0;
        }

        static bool IsRangeFullyUnreachable(bool[] reachable, int startInclusive, int endExclusive) {
            for (int i = startInclusive; i < endExclusive; i++) {
                if (reachable[i]) {
                    return false;
                }
            }
            return true;
        }

        static void AddBoundaryInstruction(HashSet<int> preservedIndices, int index, int instructionCount) {
            if (index >= 0 && index < instructionCount) {
                preservedIndices.Add(index);
            }
        }

        static void AddEhSkeletonInstructions(
            HashSet<int> preservedIndices,
            Mono.Collections.Generic.Collection<Instruction> instructions,
            int startInclusive,
            int endExclusive) {
            for (int i = startInclusive; i < endExclusive; i++) {
                if (IsEhSkeletonInstruction(instructions[i].OpCode)) {
                    preservedIndices.Add(i);
                }
            }
        }

        static void GetEhInfos(
            MethodBody body,
            Dictionary<Instruction, int> indexMap,
            int instructionCount,
            out List<ExceptionHandlerInfo> infos) {
            infos = [];
            if (!body.HasExceptionHandlers) {
                return;
            }

            foreach (ExceptionHandler? handler in body.ExceptionHandlers) {
                if (handler.TryStart is null || !indexMap.TryGetValue(handler.TryStart, out var tryStart)) {
                    continue;
                }

                int tryEnd;
                if (handler.TryEnd is null) {
                    tryEnd = instructionCount;
                }
                else if (!indexMap.TryGetValue(handler.TryEnd, out tryEnd)) {
                    continue;
                }

                if (handler.HandlerStart is null || !indexMap.TryGetValue(handler.HandlerStart, out var handlerStart)) {
                    continue;
                }

                int handlerEnd;
                if (handler.HandlerEnd is null) {
                    handlerEnd = instructionCount;
                }
                else if (!indexMap.TryGetValue(handler.HandlerEnd, out handlerEnd)) {
                    continue;
                }

                int filterStart = -1;
                int filterEnd = -1;
                if (handler.FilterStart is not null) {
                    if (!indexMap.TryGetValue(handler.FilterStart, out filterStart)) {
                        continue;
                    }
                    filterEnd = handlerStart;
                }

                if (tryStart > tryEnd || handlerStart > handlerEnd || (filterStart >= 0 && filterStart > filterEnd)) {
                    continue;
                }

                infos.Add(new ExceptionHandlerInfo(
                    handler: handler,
                    tryStart: tryStart,
                    tryEnd: tryEnd,
                    handlerStart: handlerStart,
                    handlerEnd: handlerEnd,
                    filterStart: filterStart,
                    filterEnd: filterEnd));
            }
        }

        static bool RemoveFullyUnreachableExceptionHandlers(
            MethodBody body,
            IReadOnlyList<ExceptionHandlerInfo> infos,
            bool[] reachable,
            out HashSet<ExceptionHandler> removedHandlers) {
            removedHandlers = [];
            bool changed = false;

            foreach (ExceptionHandlerInfo info in infos) {
                if (!IsRangeFullyUnreachable(reachable, info.TryStart, info.TryEnd)) {
                    continue;
                }

                if (info.HasFilter && !IsRangeFullyUnreachable(reachable, info.FilterStart, info.FilterEnd)) {
                    continue;
                }

                if (!IsRangeFullyUnreachable(reachable, info.HandlerStart, info.HandlerEnd)) {
                    continue;
                }

                body.ExceptionHandlers.Remove(info.Handler);
                removedHandlers.Add(info.Handler);
                changed = true;
            }

            return changed;
        }

        bool NopUnreachableInstructions(MethodBody body) {
            Collection<Instruction> instructions = body.Instructions;
            if (instructions.Count == 0) {
                return false;
            }

            var indexMap = new Dictionary<Instruction, int>(instructions.Count);
            for (int i = 0; i < instructions.Count; i++) {
                indexMap[instructions[i]] = i;
            }

            // Conservative EH edge: if any instruction in a try region is reachable, consider its handler/filter reachable too.
            // This is important for finally blocks (executed via "hidden" control-flow during leave/unwind).
            GetEhInfos(body, indexMap, instructions.Count, out List<ExceptionHandlerInfo>? exceptionHandlers);

            var reachable = new bool[instructions.Count];
            var work = new Stack<int>();

            reachable[0] = true;
            work.Push(0);

            while (work.TryPop(out var currentIndex)) {
                Instruction current = instructions[currentIndex];

                foreach (ExceptionHandlerInfo handler in exceptionHandlers) {
                    if (currentIndex < handler.TryStart || currentIndex >= handler.TryEnd) {
                        continue;
                    }

                    if (handler.HasFilter && !reachable[handler.FilterStart]) {
                        reachable[handler.FilterStart] = true;
                        work.Push(handler.FilterStart);
                    }

                    if (!reachable[handler.HandlerStart]) {
                        reachable[handler.HandlerStart] = true;
                        work.Push(handler.HandlerStart);
                    }
                }

                if (current.OpCode.Code == Code.Switch) {
                    if (TryResolveSwitchTargets(current.Operand, out IReadOnlyList<Instruction>? targets)) {
                        foreach (Instruction target in targets) {
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
                            if (TryResolveBranchTarget(current.Operand, out Instruction? target)
                                && target is not null
                                && indexMap.TryGetValue(target, out var targetIndex)
                                && !reachable[targetIndex]) {
                                reachable[targetIndex] = true;
                                work.Push(targetIndex);
                            }
                            break;
                        }

                    case FlowControl.Cond_Branch: {
                            if (TryResolveBranchTarget(current.Operand, out Instruction? target)
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

            bool changed = RemoveFullyUnreachableExceptionHandlers(body, exceptionHandlers, reachable, out HashSet<ExceptionHandler>? removedHandlers);

            HashSet<int> preservedEhSkeletonInstructionIndices = [];
            foreach (ExceptionHandlerInfo handler in exceptionHandlers) {
                if (removedHandlers.Contains(handler.Handler)) {
                    continue;
                }

                AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.TryStart, instructions.Count);
                AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.TryEnd, instructions.Count);
                AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.HandlerStart, instructions.Count);
                AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.HandlerEnd, instructions.Count);
                if (handler.HasFilter) {
                    AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.FilterStart, instructions.Count);
                    AddBoundaryInstruction(preservedEhSkeletonInstructionIndices, handler.FilterEnd, instructions.Count);
                }

                AddEhSkeletonInstructions(preservedEhSkeletonInstructionIndices, instructions, handler.TryStart, handler.TryEnd);
                AddEhSkeletonInstructions(preservedEhSkeletonInstructionIndices, instructions, handler.HandlerStart, handler.HandlerEnd);
                if (handler.HasFilter) {
                    AddEhSkeletonInstructions(preservedEhSkeletonInstructionIndices, instructions, handler.FilterStart, handler.FilterEnd);
                }
            }

            for (int i = 0; i < instructions.Count; i++) {
                if (reachable[i]) {
                    continue;
                }

                if (preservedEhSkeletonInstructionIndices.Contains(i)) {
                    continue;
                }

                Instruction instruction = instructions[i];
                if (instruction.OpCode.Code is Code.Nop && instruction.Operand is null) {
                    continue;
                }

                if (instruction.OpCode.Code is not Code.Ret) {
                    instruction.OpCode = OpCodes.Nop;
                    instruction.Operand = null;
                    changed = true;
                }
            }

            return changed;
        }
    }
}
