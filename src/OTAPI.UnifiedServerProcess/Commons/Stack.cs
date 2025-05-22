using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Commons {
    public static partial class MonoModCommon {
        [MonoMod.MonoModIgnore]
        public static class Stack {
            private struct AnalysisContext {
                public Instruction CurrentInstruction;
                public int TargetPosition;

                public AnalysisContext(Instruction current, int position) {
                    CurrentInstruction = current;
                    TargetPosition = position;
                }
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="caller">Executing method</param>
            /// <param name="afterThisExec">Analyze after this instruction</param>
            /// <returns>Return all possible uses of the top value on the stack/returns>
            public static Instruction[] AnalyzeStackTopValueUsage(MethodDefinition caller, Instruction afterThisExec) {
                var visited = new HashSet<(Instruction, int)>();
                var results = new HashSet<Instruction>();
                var workQueue = new Queue<AnalysisContext>();

                Instruction initialInstruction = afterThisExec.Next;

                if (initialInstruction is null) return [];

                workQueue.Enqueue(new AnalysisContext(initialInstruction, 0));

                while (workQueue.Count > 0) {
                    var ctx = workQueue.Dequeue();

                    if (visited.Contains((ctx.CurrentInstruction, ctx.TargetPosition)))
                        continue;
                    visited.Add((ctx.CurrentInstruction, ctx.TargetPosition));

                    int popCount = GetPopCount(caller.Body, ctx.CurrentInstruction);
                    int pushCount = GetPushCount(caller.Body, ctx.CurrentInstruction);
                    int currentPosition = ctx.TargetPosition;

                    if (currentPosition < popCount) {
                        if (ctx.CurrentInstruction.OpCode == OpCodes.Dup) {
                            foreach (var next in GetNextInstructions(ctx.CurrentInstruction)) {
                                if (next is null) continue;
                                workQueue.Enqueue(new AnalysisContext(next, 0));
                                workQueue.Enqueue(new AnalysisContext(next, 1));
                            }
                        }
                        else {
                            results.Add(ctx.CurrentInstruction);
                        }
                    }
                    else {
                        int newPosition = currentPosition - popCount + pushCount;
                        foreach (var next in GetNextInstructions(ctx.CurrentInstruction)) {
                            if (next != null)
                                workQueue.Enqueue(new AnalysisContext(next, newPosition));
                        }
                    }
                }

                return results.ToArray();
            }

            private static IEnumerable<Instruction> GetNextInstructions(Instruction current) {
                switch (current.OpCode.FlowControl) {
                    case FlowControl.Branch:
                        yield return (Instruction)current.Operand;
                        break;

                    case FlowControl.Cond_Branch:
                        yield return (Instruction)current.Operand;
                        yield return current.Next;
                        break;

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        yield break;

                    default:
                        yield return current.Next;
                        break;
                }
            }
            public static TypeReference? AnalyzeStackTopType(MethodDefinition caller, Instruction afterThisExec, Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {
                // Verify target is within the method's instructions
                var instructions = caller.Body.Instructions;
                int targetIndex = instructions.IndexOf(afterThisExec);
                if (targetIndex == -1)
                    throw new ArgumentException("Target instruction is not part of the method body.");

                cachedJumpSitess ??= BuildJumpSitesMap(caller);

                // initialize analysis queue
                var workQueue = new Queue<(Instruction current, int stackBalance)>();

                workQueue.Enqueue((afterThisExec, -1));

                TypeReference? type = null;

                var visited = new HashSet<(Instruction, int)>();

                while (workQueue.Count > 0) {
                    var (current, stackBalance) = workQueue.Dequeue();

                    // Check for visited state to prevent loops
                    var stateKey = (current, stackBalance);
                    if (visited.Contains(stateKey)) continue;
                    visited.Add(stateKey);

                    var newerBalance = stackBalance + GetPushCount(caller.Body, current) - GetPopCount(caller.Body, current);

                    var addCount = GetPushCount(caller.Body, current);
                    if (addCount > 1) {
                        addCount = GetPushCount(caller.Body, current) - GetPopCount(caller.Body, current);
                        if (addCount <= 0) {
                            addCount = 1;
                        }
                    }

                    if (stackBalance + addCount >= 0) {
                        if (current.OpCode != OpCodes.Dup) {
                            type = GetPushType(current, caller, cachedJumpSitess);
                            if (type is not null) {
                                break;
                            }
                            else {
                                continue;
                            }
                        }
                        else {
                            newerBalance = -1;
                        }
                    }

                    if (cachedJumpSitess.TryGetValue(current, out var jumpSitess)) {
                        foreach (var source in jumpSitess) {
                            workQueue.Enqueue((source, newerBalance));
                        }
                    }

                    // linear backtracking
                    if (current.Previous != null) {
                        if (!IsTerminatorInstruction(current.Previous)) {
                            workQueue.Enqueue((current.Previous, newerBalance));
                        }
                        else if (newerBalance == -1 && IsTryEndInstruction(caller.Body, current.Previous, out type)) {
                            break;
                        }
                    }
                }

                return type;
            }
            public static StackTopTypePath[] AnalyzeStackTopTypeAllPaths(MethodDefinition caller, Instruction afterThisExec, Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {
                var instructions = caller.Body.Instructions;
                int targetIndex = instructions.IndexOf(afterThisExec);
                if (targetIndex == -1)
                    throw new ArgumentException("Target instruction is not part of the method body.");

                cachedJumpSitess ??= BuildJumpSitesMap(caller);

                // initialize analysis queue
                var workQueue = new Queue<(List<Instruction> path, Instruction current, int stackBalance)>();

                workQueue.Enqueue(([], afterThisExec, -1));

                List<StackTopTypePath> paths = [];
                var visited = new HashSet<(Instruction, int)>();

                while (workQueue.Count > 0) {
                    var (path, current, stackBalance) = workQueue.Dequeue();

                    path.Add(current);

                    // Check for visited state to prevent loops
                    var stateKey = (current, stackBalance);
                    if (visited.Contains(stateKey)) continue;
                    visited.Add(stateKey);

                    var newerBalance = stackBalance + GetPushCount(caller.Body, current) - GetPopCount(caller.Body, current);

                    var addCount = GetPushCount(caller.Body, current);
                    if (addCount > 1) {
                        addCount = GetPushCount(caller.Body, current) - GetPopCount(caller.Body, current);
                        if (addCount <= 0) {
                            addCount = 1;
                        }
                    }

                    if (stackBalance + addCount >= 0) {
                        if (current.OpCode != OpCodes.Dup) {
                            var type = GetPushType(current, caller, cachedJumpSitess);
                            paths.Add(new StackTopTypePath(type, current) { Instructions = [.. path] });
                            continue;
                        }
                        else {
                            newerBalance = -1;
                        }
                    }

                    if (cachedJumpSitess.TryGetValue(current, out var jumpSitess)) {
                        foreach (var source in jumpSitess) {
                            workQueue.Enqueue(([.. path], source, newerBalance));
                        }
                    }

                    // linear backtracking
                    if (current.Previous != null) {
                        if (!IsTerminatorInstruction(current.Previous)) {
                            workQueue.Enqueue(([.. path], current.Previous, newerBalance));
                        }
                        else if (newerBalance == -1 && IsTryEndInstruction(caller.Body, current.Previous, out var exceptionType)) {
                            path.Add(current.Previous);
                            paths.Add(new StackTopTypePath(exceptionType, current.Previous) { Instructions = [.. path] });
                            continue;
                        }
                    }
                }

                HashSet<StackTopTypePath> stackTopTypePaths = [];

                foreach (var path in paths) {
                    path.Instructions = [.. path.Instructions.Where(inst => !IsStackEffectFree(caller.Body, inst)).Reverse()];
                    stackTopTypePaths.Add(path);
                }
                return [.. stackTopTypePaths];
            }
            public static bool CheckSinglePredecessor(
                MethodDefinition method,
                Instruction upperBound,
                Instruction lowerBound,
                Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {

                cachedJumpSitess ??= BuildJumpSitesMap(method);
                HashSet<Instruction> allowsForward = [];
                HashSet<Instruction> allowsBackward = [];
                HashSet<Instruction> allows = [];
                // Step 1: Build allowed instruction set
                var currentForward = lowerBound;
                var currentBackward = lowerBound;

                while (currentForward != null && currentBackward != null) {
                    if (currentForward is not null) {
                        allowsForward.Add(currentForward);
                        currentForward = currentForward.Previous;
                    }

                    if (currentBackward is not null) {
                        allowsBackward.Add(currentBackward);
                        currentBackward = currentBackward.Next;
                    }

                    if (currentForward == upperBound) {
                        allows = allowsForward;
                        break;
                    }

                    if (currentBackward == upperBound) {
                        (upperBound, lowerBound) = (lowerBound, upperBound);
                        allows = allowsBackward;
                        break;
                    }
                }

                if (allows.Count == 0) {
                    throw new ArgumentException("Invalid bounds.");
                }

                // Step 2: Traverse all possible predecessors
                HashSet<Instruction> visited = new();
                Stack<Instruction> works = new();
                bool reachUpperBound = false;
                works.Push(lowerBound);

                while (works.Count > 0) {
                    var work = works.Pop();
                    if (visited.Contains(work)) continue;
                    visited.Add(work);

                    // Check jump sources
                    if (cachedJumpSitess.TryGetValue(work, out var jumpSites)) {
                        foreach (var site in jumpSites) {
                            if (!allows.Contains(site)) {
                                // External jump source detected
                                return false;
                            }
                            works.Push(site);
                        }
                    }

                    // Check previous instruction
                    var previous = work.Previous;
                    if (previous is null || IsUnreachablePredecessor(previous)) {
                        // Dead end, stop tracing this path
                        continue;
                    }
                    if (previous == upperBound) {
                        reachUpperBound = true;
                        continue;
                    }

                    works.Push(previous);
                }

                return reachUpperBound;
            }
            public static bool CheckSinglePredecessor(
                MethodDefinition method,
                Instruction[] bound,
                out Instruction upperBound,
                out Instruction lowerBound,
                Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {

                if (bound.Length < 2) {
                    throw new ArgumentException("Invalid bounds.");
                }

                cachedJumpSitess ??= BuildJumpSitesMap(method);
                List<Instruction> allowsForward = [];
                List<Instruction> allowsBackward = [];

                // Step 1: Build allowed instruction set
                var currentForward = bound[bound.Length / 2 - 1];
                var currentBackward = bound[bound.Length / 2];

                Dictionary<Instruction, int> allows = new() {
                    { currentForward, 0 },
                    { currentBackward, 1 }
                };


                var boundSet = new HashSet<Instruction>(bound);

                while (true) {
                    if (currentForward is not null) {
                        allowsForward.Add(currentForward);

                        if (boundSet.Contains(currentForward)) {
                            var index = allows[allowsForward[0]];
                            for (int i = 1; i < allowsForward.Count; i++) {
                                allows[allowsForward[i]] = index - i;
                            }
                            boundSet.Remove(currentForward);
                            allowsForward.Clear();
                            allowsForward.Add(currentForward);
                        }

                        currentForward = currentForward.Previous;
                    }

                    if (currentBackward is not null) {
                        allowsBackward.Add(currentBackward);

                        if (boundSet.Contains(currentBackward)) {
                            var index = allows[allowsBackward[0]];
                            for (int i = 1; i < allowsBackward.Count; i++) {
                                allows[allowsBackward[i]] = index + i;
                            }
                            boundSet.Remove(currentBackward);
                            allowsBackward.Clear();
                            allowsBackward.Add(currentBackward);
                        }

                        currentBackward = currentBackward.Next;
                    }

                    if (boundSet.Count == 0) {
                        break;
                    }

                    if (currentForward is null && currentBackward is null) {
                        throw new ArgumentException("Invalid bounds, could not find upper and lower bound.");
                    }
                }

                if (allows.Count == 0) {
                    throw new ArgumentException("Invalid bounds.");
                }

                var sorted = allows.OrderBy(x => x.Value).Select(x => x.Key).ToArray();
                upperBound = sorted[0];
                lowerBound = sorted[^1];

                // Step 2: Traverse all possible predecessors
                HashSet<Instruction> visited = new();
                Stack<Instruction> works = new();
                bool reachUpperBound = false;

                works.Push(lowerBound);

                while (works.Count > 0) {
                    var work = works.Pop();
                    if (visited.Contains(work)) continue;
                    visited.Add(work);

                    // Check jump sources
                    if (cachedJumpSitess.TryGetValue(work, out var jumpSites)) {
                        foreach (var site in jumpSites) {
                            if (!allows.ContainsKey(site)) {
                                // External jump source detected
                                return false;
                            }
                            works.Push(site);
                        }
                    }

                    // Check previous instruction
                    var previous = work.Previous;
                    if (previous is null || IsUnreachablePredecessor(previous)) {
                        // Dead end, stop tracing this path
                        continue;
                    }

                    if (previous == upperBound) {
                        reachUpperBound = true;
                        continue;
                    }

                    works.Push(previous);
                }

                return reachUpperBound;
            }

            private static bool IsUnreachablePredecessor(Instruction instr) {
                return instr.OpCode.FlowControl switch {
                    FlowControl.Return => true, // ret
                    FlowControl.Throw => true,  // throw
                    FlowControl.Branch => true, // br/br.s
                    _ => false
                };
            }
            public static TypeReference? GetPushType(Instruction instruction, MethodDefinition method, Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {
                switch (instruction.OpCode.Code) {
                    // Load
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                        var variable = IL.GetReferencedVariable(method, instruction);
                        return variable.VariableType;
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                    case Code.Ldarg_S:
                    case Code.Ldarg:
                        var parameter = IL.GetReferencedParameter(method, instruction);
                        return parameter.ParameterType;
                    case Code.Ldfld:
                        var field = (FieldReference)instruction.Operand;
                        return field.FieldType;
                    case Code.Ldsfld:
                        field = (FieldReference)instruction.Operand;
                        return field.FieldType;
                    case Code.Call:
                    case Code.Callvirt:
                        var methodCall = (MethodReference)instruction.Operand;
                        return IL.GetMethodReturnType(methodCall, method);
                    case Code.Newobj:
                        var ctor = (MethodReference)instruction.Operand;
                        return ctor.DeclaringType;
                    case Code.Newarr:
                        return new ArrayType((TypeReference)instruction.Operand);
                    case Code.Ldnull:
                        return null; // Null reference
                    case Code.Ldstr:
                        return method.Module.TypeSystem.String;
                    case Code.Ldc_I4:
                    case Code.Ldc_I4_S:
                    case Code.Ldc_I4_0:
                    case Code.Ldc_I4_1:
                    case Code.Ldc_I4_2:
                    case Code.Ldc_I4_3:
                    case Code.Ldc_I4_4:
                    case Code.Ldc_I4_5:
                    case Code.Ldc_I4_6:
                    case Code.Ldc_I4_7:
                    case Code.Ldc_I4_8:
                    case Code.Ldc_I4_M1:
                        return method.Module.TypeSystem.Int32;
                    case Code.Ldc_I8:
                        return method.Module.TypeSystem.Int64;
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                        return method.Module.TypeSystem.Double;

                    // number calculate

                    // fixed return Int32
                    case Code.Add_Ovf:
                    case Code.Sub_Ovf:
                    case Code.Mul_Ovf:
                        return method.Module.TypeSystem.Int32;

                    // fixed return UInt32
                    case Code.Add_Ovf_Un:
                    case Code.Sub_Ovf_Un:
                    case Code.Mul_Ovf_Un:
                        return method.Module.TypeSystem.UInt32;

                    case Code.Shl:
                    case Code.Shr:
                    case Code.Shr_Un:
                    case Code.And:
                    case Code.Or:
                    case Code.Xor:
                    case Code.Not:
                    case Code.Add:
                    case Code.Sub:
                    case Code.Mul:
                    case Code.Div:
                    case Code.Div_Un:
                    case Code.Rem:
                    case Code.Rem_Un:
                    case Code.Neg:
                        var calculatePaths = Stack.AnalyzeInstructionArgsSources(method, instruction, cachedJumpSitess);
                        var operandType = Stack.AnalyzeStackTopType(method, calculatePaths[0].ParametersSources[0].Instructions.Last());
                        return operandType;

                    // number type convert
                    case Code.Conv_I1: return method.Module.TypeSystem.SByte;
                    case Code.Conv_I2: return method.Module.TypeSystem.Int16;
                    case Code.Conv_I4: return method.Module.TypeSystem.Int32;
                    case Code.Conv_I8: return method.Module.TypeSystem.Int64;
                    case Code.Conv_U1: return method.Module.TypeSystem.Byte;
                    case Code.Conv_U2: return method.Module.TypeSystem.UInt16;
                    case Code.Conv_U4: return method.Module.TypeSystem.UInt32;
                    case Code.Conv_U8: return method.Module.TypeSystem.UInt64;
                    case Code.Conv_R4: return method.Module.TypeSystem.Single;
                    case Code.Conv_R8: return method.Module.TypeSystem.Double;
                    case Code.Conv_I: return method.Module.TypeSystem.IntPtr;
                    case Code.Conv_U: return method.Module.TypeSystem.UIntPtr;
                    case Code.Conv_Ovf_I1: return method.Module.TypeSystem.SByte;
                    case Code.Conv_Ovf_I2: return method.Module.TypeSystem.Int16;
                    case Code.Conv_Ovf_I4: return method.Module.TypeSystem.Int32;
                    case Code.Conv_Ovf_I8: return method.Module.TypeSystem.Int64;
                    case Code.Conv_Ovf_U1: return method.Module.TypeSystem.Byte;
                    case Code.Conv_Ovf_U2: return method.Module.TypeSystem.UInt16;
                    case Code.Conv_Ovf_U4: return method.Module.TypeSystem.UInt32;
                    case Code.Conv_Ovf_U8: return method.Module.TypeSystem.UInt64;
                    case Code.Conv_Ovf_I: return method.Module.TypeSystem.IntPtr;
                    case Code.Conv_Ovf_U: return method.Module.TypeSystem.UIntPtr;

                    // fixed return Single
                    case Code.Conv_R_Un: return method.Module.TypeSystem.Single;

                    // reference type convert
                    case Code.Castclass:
                    case Code.Unbox:
                    case Code.Unbox_Any:
                        return (TypeReference)instruction.Operand;
                    case Code.Box:
                        return method.Module.TypeSystem.Object;
                    case Code.Isinst:
                        return method.Module.TypeSystem.Boolean;

                    // number compare
                    case Code.Ceq:
                    case Code.Cgt:
                    case Code.Cgt_Un:
                    case Code.Clt:
                    case Code.Clt_Un:
                        return method.Module.TypeSystem.Boolean;

                    // array access
                    case Code.Ldlen: return method.Module.TypeSystem.Int32;
                    case Code.Ldelem_I1: return method.Module.TypeSystem.SByte;
                    case Code.Ldelem_U1: return method.Module.TypeSystem.Byte;
                    case Code.Ldelem_I2: return method.Module.TypeSystem.Int16;
                    case Code.Ldelem_U2: return method.Module.TypeSystem.UInt16;
                    case Code.Ldelem_I4: return method.Module.TypeSystem.Int32;
                    case Code.Ldelem_U4: return method.Module.TypeSystem.UInt32;
                    case Code.Ldelem_I8: return method.Module.TypeSystem.Int64;
                    case Code.Ldelem_R4: return method.Module.TypeSystem.Single;
                    case Code.Ldelem_R8: return method.Module.TypeSystem.Double;
                    case Code.Ldelem_I: return method.Module.TypeSystem.IntPtr;
                    case Code.Ldelem_Any: return (TypeReference)instruction.Operand;
                    case Code.Ldelem_Ref:
                        var arrayAccessPaths = AnalyzeInstructionArgsSources(method, instruction).First();
                        var arrayInstructions = arrayAccessPaths.ParametersSources[0].Instructions;
                        var arrayType = AnalyzeStackTopType(method, arrayInstructions.Last(), cachedJumpSitess);
                        if (arrayType is ArrayType at) {
                            return at.ElementType;
                        }
                        throw new NotSupportedException($"Could not analyze array type: {arrayType}");

                    case Code.Ldind_I1: return method.Module.TypeSystem.SByte;
                    case Code.Ldind_I2: return method.Module.TypeSystem.Int16;
                    case Code.Ldind_I4: return method.Module.TypeSystem.Int32;
                    case Code.Ldind_I8: return method.Module.TypeSystem.Int64;
                    case Code.Ldind_U1: return method.Module.TypeSystem.Byte;
                    case Code.Ldind_U2: return method.Module.TypeSystem.UInt16;
                    case Code.Ldind_U4: return method.Module.TypeSystem.UInt32;
                    case Code.Ldind_R4: return method.Module.TypeSystem.Single;
                    case Code.Ldind_R8: return method.Module.TypeSystem.Double;
                    case Code.Ldind_I: return method.Module.TypeSystem.IntPtr;
                    case Code.Ldind_Ref: return method.Module.TypeSystem.Object;

                    case Code.Ldobj: return (TypeReference)instruction.Operand;

                    // memory access
                    case Code.Ldftn:
                    case Code.Ldvirtftn:
                        return method.Module.TypeSystem.IntPtr;
                    case Code.Ldarga:
                    case Code.Ldarga_S:
                        var param = IL.GetReferencedParameter(method, instruction);
                        return new ByReferenceType(param.ParameterType);
                    case Code.Ldloca:
                    case Code.Ldloca_S:
                        var local = IL.GetReferencedVariable(method, instruction);
                        return new ByReferenceType(local.VariableType);
                    case Code.Ldelema:
                        return new ByReferenceType((TypeReference)instruction.Operand);
                    case Code.Ldflda:
                        field = (FieldReference)instruction.Operand;
                        return new ByReferenceType(field.DeclaringType);
                    case Code.Ldsflda:
                        field = (FieldReference)instruction.Operand;
                        return new ByReferenceType(field.DeclaringType);
                    case Code.Sizeof:
                        return method.Module.TypeSystem.Int32;

                    // memory allocation
                    case Code.Localloc:
                        return method.Module.TypeSystem.IntPtr;

                    // metadata call
                    case Code.Ldtoken:
                        return method.Module.TypeSystem.IntPtr;

                    // exception handling
                    case Code.Leave:
                    case Code.Leave_S:
                        IsTryEndInstruction(method.Body, instruction, out var exceptionType);
                        return exceptionType ?? throw new NotSupportedException("Instruction is not a catch instruction.");

                    default:
                        throw new NotSupportedException($"Instruction {instruction.OpCode} not supported.");
                }
            }
            public abstract class ArgumentSource {
                public Instruction[] Instructions { get; set; } = [];
            }
            public class FlowPath<TSource>(TSource[] parametersSources) where TSource : ArgumentSource {
                public TSource[] ParametersSources { get; set; } = parametersSources;
            }
            public class StackTopTypePath(TypeReference? type, Instruction actual) : ArgumentSource, IEquatable<StackTopTypePath> {
                public TypeReference? StackTopType { get; } = type;
                /// <summary>
                /// <para> The instruction that pushed the value onto the stack. </para>
                /// <para> If the instruction is Dup, this is the instruction that dup copied the value.</para>
                /// <para>Otherwise, this is itself.</para>
                /// </summary>
                public Instruction RealPushValueInstruction => actual;

                public override string ToString() => $"[{StackTopType?.FullName}] (inst: {Instructions.Length}, hash: {GetHashCode()})";

                public override int GetHashCode() {
                    if (Instructions.Length == 0) {
                        return -1;
                    }
                    return HashCode.Combine(Instructions.First(), Instructions.Last());
                }
                public override bool Equals(object? obj) {
                    if (obj is StackTopTypePath other) {
                        return Instructions.First() == other.Instructions.First() && Instructions.Last() == other.Instructions.Last();
                    }
                    return false;
                }
                public bool Equals(StackTopTypePath? other) {
                    if (other is null) return false;
                    return Instructions.First() == other.Instructions.First() && Instructions.Last() == other.Instructions.Last();
                }
            }
            public class ParameterSource(ParameterDefinition parameter) : ArgumentSource {
                public ParameterDefinition Parameter { get; } = parameter;

                public override string ToString() => $"[{Parameter.Name}:{Parameter.ParameterType.Name}] (inst: {Instructions.Length})";
            }
            public class InstructionArgsSource(int index) : ArgumentSource {
                public int Index { get; } = index;

                public override string ToString() => $"[{Index}] (inst: {Instructions.Length})";
            }
            /// <summary>
            /// Analyzes parameter sources considering control flow and multiple execution paths
            /// </summary>
            /// <param name="caller">Containing method</param>
            /// <param name="target">Method call/newobj instruction</param>
            /// <returns>Array of possible parameter flow paths</returns>
            public static FlowPath<ParameterSource>[] AnalyzeParametersSources(MethodDefinition caller, Instruction target, Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {

                cachedJumpSitess ??= BuildJumpSitesMap(caller);

                // Resolve method signature
                var callee = (MethodReference)target.Operand;
                bool hasThis = (target.OpCode == OpCodes.Call || target.OpCode == OpCodes.Callvirt) && callee.HasThis;
                int paramCount = callee.Parameters.Count + (hasThis ? 1 : 0);
                if (target.OpCode == OpCodes.Newobj)
                    paramCount = callee.Parameters.Count;

                // Initialize analysis queue
                var workQueue = new Queue<ReverseAnalysisContext>();
                var initialDemand = new StackDemand(paramCount, GetPushCount(caller.Body, target));
                workQueue.Enqueue(new ReverseAnalysisContext(
                    current: target,
                    previous: target.Previous,
                    stackDemand: initialDemand,
                    path: new List<Instruction>(),
                    isBranch: false
                ));

                List<FlowPath<ParameterSource>> paths = new();
                var visited = new HashSet<(Instruction, int)>(); // Track visited (offset, stackBalance)

                while (workQueue.Count > 0) {
                    var ctx = workQueue.Dequeue();

                    // Check for visited state to prevent loops
                    var stateKey = (ctx.Current, ctx.StackDemand.StackBalance);
                    if (visited.Contains(stateKey)) continue;
                    visited.Add(stateKey);

                    // Clone context to prevent state pollution
                    var stackDemand = ctx.StackDemand.Clone();
                    var path = new List<Instruction>(ctx.Path);

                    // Process current instruction's reverse stack effect
                    int originalPush = GetPushCount(caller.Body, ctx.Current);
                    int originalPop = GetPopCount(caller.Body, ctx.Current);

                    if (!stackDemand.ApplyInstruction(
                        pop: originalPush,
                        push: originalPop)) {
                        continue; // Invalid stack state
                    }
                    path.Add(ctx.Current);

                    // Termination condition
                    if (stackDemand.StackBalance == 0) {
                        path.Reverse();
                        paths.Add(ProcessCompletedPath(caller, path, callee));
                        continue;
                    }

                    // Handle branching paths
                    if (cachedJumpSitess.TryGetValue(ctx.Current, out var jumpSitess)) {
                        foreach (var source in jumpSitess) {
                            var branchStack = stackDemand.Clone();
                            var branchPath = new List<Instruction>(path);
                            workQueue.Enqueue(new ReverseAnalysisContext(
                                current: source,
                                previous: source.Previous,
                                stackDemand: branchStack,
                                path: branchPath,
                                isBranch: true
                            ));
                        }
                    }

                    // Linear backtracking
                    if (ctx.Previous != null) {
                        if (!IsTerminatorInstruction(ctx.Previous)) {
                            workQueue.Enqueue(new ReverseAnalysisContext(
                                current: ctx.Previous,
                                previous: ctx.Previous.Previous,
                                stackDemand: stackDemand,
                                path: path,
                                isBranch: false
                            ));
                        }
                        else if (stackDemand.StackBalance == -1 && IsTryEndInstruction(caller.Body, ctx.Previous, out _)) {
                            path.Add(ctx.Previous);
                            path.Reverse();
                            paths.Add(ProcessCompletedPath(caller, path, callee));
                            continue;
                        }
                    }
                }

                return BuildFlowPaths(caller.Body, paths);
            }
            private static int GetInstructionArgCount(MethodBody body, Instruction instruction) {
                switch (instruction.OpCode.FlowControl) {
                    case FlowControl.Branch when instruction.OpCode == OpCodes.Switch:
                        return 1;  // switch instruction needs 1 operand (selector value)
                    case FlowControl.Cond_Branch:
                        return GetConditionalBranchArgCount(body, instruction);
                }

                return GetPopCount(body, instruction);
            }

            private static int GetConditionalBranchArgCount(MethodBody body, Instruction instruction) {
                // 条件跳转指令的特殊处理
                // Special handling for conditional branch instructions
                return instruction.OpCode.Code switch {
                    Code.Brtrue or Code.Brtrue_S => 1,  // 需要1个布尔值 // need 1 boolean
                    Code.Brfalse or Code.Brfalse_S => 1,
                    Code.Beq or Code.Beq_S => 2,        // 需要两个比较值 // need 2 comparison values
                    Code.Bge or Code.Bge_S => 2,
                    Code.Bge_Un or Code.Bge_Un_S => 2,
                    Code.Bgt or Code.Bgt_S => 2,
                    Code.Bgt_Un or Code.Bgt_Un_S => 2,
                    Code.Ble or Code.Ble_S => 2,
                    Code.Ble_Un or Code.Ble_Un_S => 2,
                    Code.Blt or Code.Blt_S => 2,
                    Code.Blt_Un or Code.Blt_Un_S => 2,
                    Code.Bne_Un or Code.Bne_Un_S => 2,
                    _ => GetPopCount(body, instruction)
                };
            }
            public static FlowPath<InstructionArgsSource>[] AnalyzeInstructionArgsSources(MethodDefinition caller, Instruction target, Dictionary<Instruction, List<Instruction>>? cachedJumpSitess = null) {

                cachedJumpSitess ??= BuildJumpSitesMap(caller);

                var argsCount = GetInstructionArgCount(caller.Body, target);

                // 初始化分析队列
                // initialize analysis queue
                var workQueue = new Queue<ReverseAnalysisContext>();
                var initialDemand = new StackDemand(argsCount, GetPushCount(caller.Body, target));

                workQueue.Enqueue(new ReverseAnalysisContext(
                    current: target,
                    previous: target.Previous,
                    stackDemand: initialDemand,
                    path: [],
                    isBranch: false
                ));

                List<FlowPath<InstructionArgsSource>> paths = new();
                var visited = new HashSet<(Instruction, int)>();

                while (workQueue.Count > 0) {
                    var ctx = workQueue.Dequeue();

                    // Check for visited state to prevent loops
                    var stateKey = (ctx.Current, ctx.StackDemand.StackBalance);
                    if (visited.Contains(stateKey)) continue;
                    visited.Add(stateKey);

                    // Clone context to prevent polluting
                    var stackDemand = ctx.StackDemand.Clone();
                    var path = new List<Instruction>(ctx.Path);

                    // Process the reverse stack effect of the current instruction
                    int originalPush = GetPushCount(caller.Body, ctx.Current);
                    int originalPop = GetPopCount(caller.Body, ctx.Current);

                    if (!stackDemand.ApplyInstruction(
                        pop: originalPush,
                        push: originalPop))
                        continue;

                    path.Add(ctx.Current);

                    // All parameters have been resolved
                    if (stackDemand.StackBalance == 0) {
                        path.Reverse();
                        paths.Add(ProcessCompletedInstructionPath(caller, path, argsCount));
                        continue;
                    }

                    // Process branch paths
                    if (cachedJumpSitess.TryGetValue(ctx.Current, out var jumpSitess)) {
                        foreach (var source in jumpSitess) {
                            var branchStack = stackDemand.Clone();
                            var branchPath = new List<Instruction>(path);

                            workQueue.Enqueue(new ReverseAnalysisContext(
                                current: source,
                                previous: source.Previous,
                                stackDemand: branchStack,
                                path: branchPath,
                                isBranch: true
                            ));
                        }
                    }

                    // 线性回溯
                    // Linear backtracking
                    if (ctx.Previous != null) {
                        if (!IsTerminatorInstruction(ctx.Previous)) {
                            workQueue.Enqueue(new ReverseAnalysisContext(
                                current: ctx.Previous,
                                previous: ctx.Previous.Previous,
                                stackDemand: stackDemand,
                                path: path,
                                isBranch: false
                            ));
                        }
                        else if (stackDemand.StackBalance == -1 && IsTryEndInstruction(caller.Body, ctx.Previous, out _)) {
                            path.Add(ctx.Previous);
                            path.Reverse();
                            paths.Add(ProcessCompletedInstructionPath(caller, path, argsCount));
                            continue;
                        }
                    }
                }

                return BuildFlowPaths(caller.Body, paths);
            }
            public static Dictionary<Instruction, List<Instruction>> BuildJumpSitesMap(MethodDefinition method) {
                var jumpTargets = new Dictionary<Instruction, List<Instruction>>();
                foreach (var instruction in method.Body.Instructions) {
                    if (instruction.Operand is ILLabel label) {
                        var target = label.Target;
                        if (target is null) {
                            continue;
                        }
                        if (!jumpTargets.TryGetValue(target!, out var sources)) {
                            sources = [];
                            jumpTargets.Add(target, sources);
                        }
                        sources.Add(instruction);
                    }
                    else if (instruction.Operand is Instruction target) {
                        if (!jumpTargets.TryGetValue(target, out var sources)) {
                            sources = [];
                            jumpTargets.Add(target, sources);
                        }
                        sources.Add(instruction);
                    }
                    else if (instruction.Operand is Instruction[] targets) {
                        foreach (var t in targets) {
                            if (!jumpTargets.TryGetValue(t, out var sources)) {
                                sources = [];
                                jumpTargets.Add(t, sources);
                            }
                            sources.Add(instruction);
                        }
                    }
                }
                return jumpTargets;
            }
            private static FlowPath<ParameterSource> ProcessCompletedPath(MethodDefinition caller, List<Instruction> path, MethodReference callee) {
                return new FlowPath<ParameterSource>([.. AnalyzeMethodCallPath(caller, callee, path.Last(), [.. path])]);
            }
            private static FlowPath<InstructionArgsSource> ProcessCompletedInstructionPath(MethodDefinition caller, List<Instruction> path, int argsCount) {
                return new FlowPath<InstructionArgsSource>(
                    [.. AnalyzeInstructionArgs(caller.Body, path.Last(), argsCount, [.. path])]
                );
            }
            private static List<ParameterSource> AnalyzeMethodCallPath(MethodDefinition caller, MethodReference callee, Instruction target, Instruction[] path) {
                path = [.. path.Take(path.Length - 1)];

                var deltas = ComputeStackDeltas(caller.Body, path);

                bool hasThis = (target.OpCode == OpCodes.Call || target.OpCode == OpCodes.Callvirt) && callee.HasThis;
                int paramCount = callee.Parameters.Count + (hasThis ? 1 : 0);

                if (target.OpCode == OpCodes.Newobj)
                    paramCount = callee.Parameters.Count;

                var parameters = new List<ParameterSource>();
                int currentIndex = path.Length - 1;

                for (int i = 0; i < paramCount; i++) {
                    if (currentIndex < 0) break;

                    var (start, end) = FindArgRange(path, deltas, currentIndex);
                    if (start == -1) break;

                    var paramInstructions = new List<Instruction>();
                    for (int j = start; j <= end; j++)
                        paramInstructions.Add(path[j]);

                    ParameterDefinition parameter;
                    if (hasThis) {
                        if (i == paramCount - 1) {
                            parameter = new ParameterDefinition("this", ParameterAttributes.None, callee.DeclaringType);
                        }
                        else {
                            parameter = callee.Parameters[paramCount - 2 - i];
                        }
                    }
                    else {
                        parameter = callee.Parameters[paramCount - 1 - i];
                    }

                    parameters.Add(new ParameterSource(parameter) { Instructions = [.. paramInstructions] });
                    currentIndex = start - 1;
                }

                parameters.Reverse();
                return parameters;
            }
            private static List<InstructionArgsSource> AnalyzeInstructionArgs(MethodBody body, Instruction target, int argsCount, Instruction[] path) {
                path = [.. path.Take(path.Length - 1)];
                var deltas = ComputeStackDeltas(body, path);
                var argsSources = new List<InstructionArgsSource>();

                int currentIndex = path.Length - 1;

                for (int i = 0; i < argsCount; i++) {
                    if (currentIndex < 0) break;

                    var (start, end) = FindArgRange(path, deltas, currentIndex);
                    if (start == -1) break;

                    var argInstructions = new List<Instruction>();
                    for (int j = start; j <= end; j++)
                        argInstructions.Add(path[j]);

                    argsSources.Add(new InstructionArgsSource(i) {
                        Instructions = argInstructions.ToArray()
                    });

                    currentIndex = start - 1;
                }

                argsSources.Reverse();
                return argsSources;
            }
            private static (int start, int end) FindArgRange(Instruction[] path, int[] deltas, int startIndex) {
                int accumulated = 0;
                int end = startIndex;
                int start = startIndex;

                while (start >= 0) {
                    accumulated += deltas[start];
                    if (accumulated == 1) break;
                    if (accumulated > 1) return (-1, -1);
                    start--;
                }

                return start >= 0 ? (start, end) : (-1, -1);
            }
            static bool IsStackEffectFree(MethodBody body, Instruction instruction) {
                switch (instruction.OpCode.Code) {
                    case Code.Br:
                    case Code.Br_S:
                    case Code.Nop:
                        return true;
                    case Code.Leave:
                    case Code.Leave_S:
                        foreach (var handler in body.ExceptionHandlers) {
                            if (handler.TryEnd.Previous == instruction) {
                                return false;
                            }
                        }
                        return true;
                    default:
                        return false;
                }
            }
            static bool IsTerminatorInstruction(Instruction instruction) {
                return instruction.OpCode.Code switch {
                    Code.Ret or Code.Throw or Code.Br or Code.Br_S or Code.Leave or Code.Leave_S => true,
                    _ => false,
                };
            }
            static bool IsTryEndInstruction(MethodBody body, Instruction instruction, [NotNullWhen(true)] out TypeReference? exceptionType) {
                if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S) {
                    foreach (var handler in body.ExceptionHandlers) {
                        if (handler.TryEnd.Previous == instruction) {
                            exceptionType = handler.CatchType;
                            return true;
                        }
                    }
                }

                exceptionType = null;
                return false;
            }
            private static FlowPath<TSource>[] BuildFlowPaths<TSource>(MethodBody body, List<FlowPath<TSource>> rawPaths) where TSource : ArgumentSource {
                foreach (var path in rawPaths) {
                    foreach (var paramSource in path.ParametersSources) {
                        paramSource.Instructions = [.. paramSource.Instructions.Where(inst => !IsStackEffectFree(body, inst))];
                    }
                }

                return [.. rawPaths];
            }
            public static int GetPopCount(MethodBody body, Instruction instruction) {
                if (instruction.OpCode == OpCodes.Ret && body.Method.ReturnType.FullName != body.Method.Module.TypeSystem.Void.FullName) {
                    return 1;
                }
                switch (instruction.OpCode.StackBehaviourPop) {
                    case StackBehaviour.Pop0:
                        return 0;
                    case StackBehaviour.Pop1:
                    case StackBehaviour.Popi:
                    case StackBehaviour.Popref:
                        return 1;
                    case StackBehaviour.Pop1_pop1:
                    case StackBehaviour.Popi_pop1:
                    case StackBehaviour.Popi_popi:
                    case StackBehaviour.Popi_popi8:
                    case StackBehaviour.Popi_popr4:
                    case StackBehaviour.Popi_popr8:
                    case StackBehaviour.Popref_pop1:
                    case StackBehaviour.Popref_popi:
                        return 2;
                    case StackBehaviour.Popi_popi_popi:
                    case StackBehaviour.Popref_popi_popr4:
                    case StackBehaviour.Popref_popi_popr8:
                    case StackBehaviour.Popref_popi_popi:
                    case StackBehaviour.Popref_popi_popi8:
                    case StackBehaviour.Popref_popi_popref:
                        return 3;
                    case StackBehaviour.PopAll:
                        return 0;
                    case StackBehaviour.Varpop:
                        if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj) {
                            var methodRef = (MethodReference)instruction.Operand;
                            int count = methodRef.Parameters.Count;
                            if (instruction.OpCode != OpCodes.Newobj && methodRef.HasThis)
                                count++;
                            return count;
                        }
                        else if (instruction.OpCode == OpCodes.Ret) {
                            var method = instruction.Operand as MethodReference ?? instruction.Operand as MethodDefinition;
                            return method != null && method.ReturnType.FullName != "System.Void" ? 1 : 0;
                        }
                        return 0;
                    default:
                        return 0;
                }
            }
            public static int GetPushCount(MethodBody body, Instruction instruction) {

                if ((instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S) &&
                    body.ExceptionHandlers.Any(handler => handler.TryEnd.Previous == instruction)) {
                    return 1;
                }

                switch (instruction.OpCode.StackBehaviourPush) {
                    case StackBehaviour.Push0:
                        return 0;
                    case StackBehaviour.Push1:
                    case StackBehaviour.Pushi:
                    case StackBehaviour.Pushi8:
                    case StackBehaviour.Pushr4:
                    case StackBehaviour.Pushr8:
                    case StackBehaviour.Pushref:
                        return 1;
                    case StackBehaviour.Push1_push1:
                        return 2;
                    case StackBehaviour.Varpush:
                        if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) {
                            var method = (MethodReference)instruction.Operand;
                            return method.ReturnType.FullName == "System.Void" ? 0 : 1;
                        }
                        else if (instruction.OpCode == OpCodes.Newobj) {
                            return 1;
                        }
                        return 0;
                    default:
                        return 0;
                }
            }
            private static int[] ComputeStackDeltas(MethodBody body, Instruction[] path) {
                return [.. path.Select(p => GetStackDelta(body, p))];
            }
            private static int GetStackDelta(MethodBody body, Instruction instruction) {
                int pop = GetPopCount(body, instruction);
                int push = GetPushCount(body, instruction);
                return push - pop;
            }
            private class ReverseAnalysisContext {
                public Instruction Current { get; }
                public Instruction Previous { get; }
                public StackDemand StackDemand { get; }
                public List<Instruction> Path { get; }
                public bool IsBranch { get; }

                public ReverseAnalysisContext(Instruction current, Instruction previous,
                    StackDemand stackDemand, List<Instruction> path, bool isBranch) {
                    Current = current;
                    Previous = previous;
                    StackDemand = stackDemand;
                    Path = path;
                    IsBranch = isBranch;
                }
            }
            private class StackDemand {
                public int ParametersToResolve { get; private set; }
                public int PushCount { get; private set; }
                public int StackBalance { get; private set; }

                public StackDemand(int popCount, int pushCount) {
                    ParametersToResolve = popCount;
                    PushCount = pushCount;
                    StackBalance = -pushCount;
                }

                public bool ApplyInstruction(int push, int pop) {
                    StackBalance += pop - push;
                    return true;
                }

                public StackDemand Clone() {
                    return new StackDemand(ParametersToResolve, PushCount) {
                        StackBalance = StackBalance
                    };
                }
            }
        }
    }
}
