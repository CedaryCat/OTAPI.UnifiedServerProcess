using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching
{
    public static class InstructionSourceCollector
    {
        public readonly record struct LocalPropagationOptions(
            Func<VariableDefinition, bool> TryVisitLocal,
            bool FollowReferenceTypeLoadConsumers = true,
            bool FollowValueTypeLoadConsumers = false,
            bool FollowAddressLoadConsumers = true);

        public static void CollectTransitiveUsageSources(
            IJumpSitesCacheFeature feature,
            MethodDefinition method,
            HashSet<Instruction> collected,
            Instruction instruction) {

            Stack<Instruction> worklist = [];
            worklist.Push(instruction);

            while (worklist.Count > 0) {
                Instruction current = worklist.Pop();
                Instruction[] usages = MonoModCommon.Stack.TraceStackValueConsumers(method, current);
                CollectSources(feature, method, collected, usages);
                foreach (Instruction usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(method.Body, usage) > 0) {
                        worklist.Push(usage);
                    }
                }
            }
        }

        public static void CollectTransitiveUsageSources(
            IJumpSitesCacheFeature feature,
            MethodDefinition method,
            HashSet<Instruction> collected,
            LocalPropagationOptions localPropagation,
            Instruction instruction) {

            Stack<Instruction> worklist = [];
            worklist.Push(instruction);

            while (worklist.Count > 0) {
                Instruction current = worklist.Pop();
                Instruction[] usages = MonoModCommon.Stack.TraceStackValueConsumers(method, current);
                CollectSources(feature, method, collected, localPropagation, usages);
                foreach (Instruction usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(method.Body, usage) > 0) {
                        worklist.Push(usage);
                    }
                }
            }
        }

        public static void CollectSources(
            IJumpSitesCacheFeature feature,
            MethodDefinition method,
            HashSet<Instruction> collected,
            params IEnumerable<Instruction> sourceSeeds) {

            Dictionary<Instruction, List<Instruction>> jumpSites = feature.GetMethodJumpSites(method);

            Stack<Instruction> worklist = [];
            foreach (Instruction sourceSeed in sourceSeeds) {
                worklist.Push(sourceSeed);
            }
            while (worklist.Count > 0) {
                Instruction current = worklist.Pop();
                if (!collected.Add(current)) {
                    continue;
                }

                QueueArgumentSources(method, jumpSites, worklist, current);
            }
        }

        /// <summary>
        /// Maps instructions in a forward conditional branch body to the branch instructions
        /// whose conditions control whether those instructions execute.
        /// </summary>
        public static Dictionary<Instruction, HashSet<Instruction>> BuildBranchBlockToConditionsMap(MethodDefinition method) {
            Dictionary<Instruction, HashSet<Instruction>> conditionBranchInstructions = [];
            Dictionary<Instruction, HashSet<Instruction>> branchBlockMapToConditions = [];
            Dictionary<Instruction, int> instructionIndices = method.Body.Instructions
                .Select((instruction, index) => (instruction, index))
                .ToDictionary(item => item.instruction, item => item.index);

            Dictionary<Instruction, (Instruction Next, HashSet<Instruction> Block)> currentProcessing = [];
            foreach (Instruction instruction in method.Body.Instructions) {
                foreach (KeyValuePair<Instruction, (Instruction Next, HashSet<Instruction> Block)> current in currentProcessing.ToArray()) {
                    if (current.Value.Next == instruction) {
                        conditionBranchInstructions[current.Key] = current.Value.Block;
                        currentProcessing.Remove(current.Key);
                    }
                    else {
                        current.Value.Block.Add(instruction);
                    }
                }

                if (instruction.Operand is Instruction jumpTarget
                    && MonoModCommon.Stack.GetPopCount(method.Body, instruction) > 0
                    && instructionIndices[jumpTarget] > instructionIndices[instruction]) {
                    currentProcessing.Add(instruction, (jumpTarget, []));
                }
            }

            if (currentProcessing.Count > 0) {
                throw new InvalidOperationException($"Could not close all forward conditional branches in {method.FullName}.");
            }

            foreach (KeyValuePair<Instruction, HashSet<Instruction>> conditionalBranch in conditionBranchInstructions) {
                foreach (Instruction instruction in conditionalBranch.Value) {
                    if (!branchBlockMapToConditions.TryGetValue(instruction, out HashSet<Instruction>? conditions)) {
                        branchBlockMapToConditions[instruction] = conditions = [];
                    }
                    conditions.Add(conditionalBranch.Key);
                }
            }

            return branchBlockMapToConditions;
        }

        public static void CollectSources(
            IJumpSitesCacheFeature feature,
            MethodDefinition method,
            HashSet<Instruction> collected,
            LocalPropagationOptions localPropagation,
            params IEnumerable<Instruction> sourceSeeds) {

            Dictionary<Instruction, List<Instruction>> jumpSites = feature.GetMethodJumpSites(method);

            Stack<Instruction> worklist = [];
            foreach (Instruction sourceSeed in sourceSeeds) {
                worklist.Push(sourceSeed);
            }
            while (worklist.Count > 0) {
                Instruction current = worklist.Pop();
                if (!collected.Add(current)) {
                    continue;
                }

                QueueArgumentSources(method, jumpSites, worklist, current);
                QueueLocalPropagationSources(method, collected, worklist, localPropagation, current);
            }
        }

        static void QueueArgumentSources(
            MethodDefinition method,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            Stack<Instruction> worklist,
            Instruction current) {

            if (current.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path in MonoModCommon.Stack.AnalyzeParametersSources(method, current, jumpSites)) {
                    foreach (MonoModCommon.Stack.ParameterSource source in path.ParametersSources) {
                        foreach (Instruction inst in source.Instructions) {
                            worklist.Push(inst);
                        }
                    }
                }
            }
            else if (MonoModCommon.Stack.GetPopCount(method.Body, current) > 0) {
                foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, current, jumpSites)) {
                    foreach (MonoModCommon.Stack.InstructionArgsSource source in path.ParametersSources) {
                        foreach (Instruction inst in source.Instructions) {
                            worklist.Push(inst);
                        }
                    }
                }
            }
        }

        static void QueueLocalPropagationSources(
            MethodDefinition method,
            HashSet<Instruction> collected,
            Stack<Instruction> worklist,
            LocalPropagationOptions localPropagation,
            Instruction current) {

            if (!MonoModCommon.IL.TryGetReferencedVariable(method, current, out VariableDefinition? local) ||
                !localPropagation.TryVisitLocal(local)) {
                return;
            }

            foreach (Instruction inst in method.Body.Instructions) {
                if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out VariableDefinition? otherLocal) ||
                    otherLocal.Index != local.Index) {
                    continue;
                }

                switch (inst.OpCode.Code) {
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                    case Code.Stloc_S:
                    case Code.Stloc:
                        worklist.Push(inst);
                        break;

                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                        collected.Add(inst);
                        if ((localPropagation.FollowReferenceTypeLoadConsumers && !local.VariableType.IsTruelyValueType()) ||
                            (localPropagation.FollowValueTypeLoadConsumers && local.VariableType.IsTruelyValueType())) {
                            foreach (Instruction usage in MonoModCommon.Stack.TraceStackValueConsumers(method, inst)) {
                                worklist.Push(usage);
                            }
                        }
                        break;

                    case Code.Ldloca_S:
                    case Code.Ldloca:
                        collected.Add(inst);
                        if (localPropagation.FollowAddressLoadConsumers) {
                            foreach (Instruction usage in MonoModCommon.Stack.TraceStackValueConsumers(method, inst)) {
                                worklist.Push(usage);
                            }
                        }
                        break;
                }
            }
        }
    }
}
