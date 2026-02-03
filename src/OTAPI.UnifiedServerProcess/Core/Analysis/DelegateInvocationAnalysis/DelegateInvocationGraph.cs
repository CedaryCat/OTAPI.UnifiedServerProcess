using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis
{
    public class DelegateInvocationGraph : Analyzer
    {
        public override string Name => "DelegateInvocation";
        /// <summary>
        /// The delegate traces, use <see cref="DelegateInvocationData.GenerateStackKey(MethodDefinition, Instruction)"/> to generate the key
        /// </summary>
        public readonly Dictionary<string, DelegateInvocationData> TracedDelegates;
        /// <summary>
        /// All delegate invocations in the module
        /// </summary>
        public readonly Dictionary<string, MethodDefinition> AllInvocations;
        public DelegateInvocationGraph(ILogger logger, ModuleDefinition module, MethodInheritanceGraph methodInherits) : base(logger) {

            Dictionary<string, DelegateInvocationData> delegateTraces = new();

            var workQueue = new Dictionary<string, MethodDefinition>(
                module.GetAllTypes()
                    .SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .ToDictionary(m => m.GetIdentifier())
            );

            int iteration = 0;
            while (workQueue.Count > 0) {
                iteration++;
                var currentWorkBatch = workQueue.Values.ToArray();

                for (int progress = 0; progress < currentWorkBatch.Length; progress++) {
                    var method = currentWorkBatch[progress];
                    Progress(iteration, progress, currentWorkBatch.Length, method.GetDebugName());
                    ProcessMethod(
                        module,
                        methodInherits,
                        method,
                        delegateTraces,
                        out var methods
                    );
                    workQueue.Remove(method.GetIdentifier());

                    if (methods.Length > 0) {
                        foreach (var item in methods) {
                            if (workQueue.TryAdd(item.GetIdentifier(), item)) {
                                Progress(iteration, progress, currentWorkBatch.Length, "Add: {0}", indent: 1, item.GetDebugName());
                            }
                        }
                    }
                }
            }

            TracedDelegates = delegateTraces.ToDictionary();

            Dictionary<string, MethodDefinition> allInvocations = [];
            foreach (var invocation in delegateTraces.Values.SelectMany(x => x.Invocations.Values)) {
                allInvocations.TryAdd(invocation.GetIdentifier(), invocation);
            }

            AllInvocations = allInvocations.ToDictionary();
        }

        private void ProcessMethod(ModuleDefinition module, MethodInheritanceGraph methodInherits, MethodDefinition processingMethod, Dictionary<string, DelegateInvocationData> delegateTraces, out MethodDefinition[] requiredReloads) {
            requiredReloads = [];

            if (!processingMethod.HasBody) {
                return;
            }

            // Skip event add and remove
            if (processingMethod.Name.OrdinalStartsWith("add_")) {
                var field = processingMethod.DeclaringType.Fields.FirstOrDefault(f => f.Name == processingMethod.Name[4..]);
                if (field is not null && field.FieldType.IsDelegate()) {
                    return;
                }
            }
            else if (processingMethod.Name.OrdinalStartsWith("remove_")) {
                var field = processingMethod.DeclaringType.Fields.FirstOrDefault(f => f.Name == processingMethod.Name[7..]);
                if (field is not null && field.FieldType.IsDelegate()) {
                    return;
                }
            }

            Dictionary<string, MethodDefinition> shouldReloads = [];

            var jumpSitess = this.GetMethodJumpSites(processingMethod);

            bool internalChanged;
            do {
                internalChanged = false;

                foreach (var instruction in processingMethod.Body.Instructions) {
                    switch (instruction.OpCode.Code) {
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                        case Code.Stloc: {
                                var variable = MonoModCommon.IL.GetReferencedVariable(processingMethod, instruction);
                                var variableType = variable.VariableType.TryResolve();

                                if (variableType is null || !variableType.IsDelegate()) {
                                    break;
                                }

                                var loadVariable = MonoModCommon.IL.BuildVariableLoad(processingMethod, processingMethod.Body, variable);

                                if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadVariable), out var trace)) {
                                    trace = new DelegateInvocationData(processingMethod, loadVariable);
                                    delegateTraces.Add(trace.Key, trace);
                                }

                                foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                    foreach (var pushValueInstrustions in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                                        var pushValue = pushValueInstrustions.RealPushValueInstruction;

                                        if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, pushValue), out var combinedFrom)) {
                                            if (trace.AddCombinedFrom(combinedFrom)) {
                                                internalChanged = true;
                                            }
                                        }
                                    }
                                }

                                break;
                            }
                        case Code.Stfld:
                        case Code.Stsfld: {
                                var field = (FieldReference)instruction.Operand;
                                var fieldType = field.FieldType.TryResolve();

                                if (fieldType is null || !fieldType.IsDelegate()) {
                                    break;
                                }

                                var loadField = Instruction.Create(instruction.OpCode == OpCodes.Stfld ? OpCodes.Ldfld : OpCodes.Ldsfld, field);

                                if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadField), out var trace)) {
                                    trace = new DelegateInvocationData(processingMethod, loadField);
                                    delegateTraces.Add(trace.Key, trace);
                                }

                                int valueIndex = 0;
                                if (instruction.OpCode == OpCodes.Stfld) {
                                    valueIndex = 1;
                                }

                                foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                    foreach (var pushValueInstrustions in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[valueIndex].Instructions.Last(), jumpSitess)) {
                                        var pushValue = pushValueInstrustions.RealPushValueInstruction;

                                        if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, pushValue), out var combinedFrom)) {
                                            if (trace.AddCombinedFrom(combinedFrom)) {
                                                internalChanged = true;
                                            }
                                        }
                                    }
                                }

                                break;
                            }
                        case Code.Call:
                        case Code.Callvirt:
                        case Code.Newobj: {
                                bool isNewObj = instruction.OpCode == OpCodes.Newobj;
                                var callingMethod = ((MethodReference)instruction.Operand).TryResolve();

                                // Defination in unloaded assembly, just skip
                                if (callingMethod is null) {
                                    break;
                                }

                                // Delegate from combine
                                if (callingMethod.Name == nameof(MulticastDelegate.Combine) && processingMethod.DeclaringType.IsDelegate()) {

                                    if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(callingMethod, instruction), out var trace)) {
                                        trace = new DelegateInvocationData(processingMethod, instruction);
                                        delegateTraces.Add(trace.Key, trace);
                                    }

                                    foreach (var argsPath in MonoModCommon.Stack.AnalyzeParametersSources(processingMethod, instruction, jumpSitess)) {
                                        foreach (var loadValue in argsPath.ParametersSources.Select(source => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, source.Instructions.Last(), jumpSitess)).SelectMany(path => path)) {
                                            if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(callingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                if (trace.AddCombinedFrom(combinedFrom)) {
                                                    internalChanged = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                // Delegate from event add
                                else if (callingMethod.Name.OrdinalStartsWith("add_") && (callingMethod.DeclaringType.FindField(callingMethod.Name[4..])?.FieldType.IsDelegate() ?? false)) {
                                    List<DelegateInvocationData> combinedFroms = new();

                                    var field = callingMethod.DeclaringType.FindField(callingMethod.Name[4..])!;

                                    foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                        foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                                            if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                combinedFroms.Add(combinedFrom);
                                            }
                                        }
                                    }

                                    var loadField = Instruction.Create(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);

                                    if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadField), out var trace)) {
                                        trace = new DelegateInvocationData(processingMethod, loadField);
                                        delegateTraces.Add(trace.Key, trace);
                                    }

                                    foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                        foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                                            if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                if (trace.AddCombinedFrom(combinedFrom)) {
                                                    internalChanged = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (callingMethod.Name.OrdinalStartsWith("remove_") && (callingMethod.DeclaringType.FindField(callingMethod.Name[7..])?.FieldType.IsDelegate() ?? false)) {
                                    List<DelegateInvocationData> combinedFroms = new();

                                    var field = callingMethod.DeclaringType.FindField(callingMethod.Name[7..])!;

                                    foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                        foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                                            if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                combinedFroms.Add(combinedFrom);
                                            }
                                        }
                                    }

                                    var loadField = Instruction.Create(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);

                                    if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadField), out var trace)) {
                                        trace = new DelegateInvocationData(processingMethod, loadField);
                                        delegateTraces.Add(trace.Key, trace);
                                    }

                                    foreach (var argsPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(processingMethod, instruction, jumpSitess)) {
                                        foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                                            if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                if (trace.AddCombinedFrom(combinedFrom)) {
                                                    internalChanged = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                // Delegate from constructor
                                else if (callingMethod.IsConstructor && callingMethod.DeclaringType.IsDelegate()) {

                                    if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, instruction), out var trace)) {
                                        trace = new DelegateInvocationData(processingMethod, instruction);
                                        delegateTraces.Add(trace.Key, trace);
                                    }

                                    var argsPaths = MonoModCommon.Stack.AnalyzeParametersSources(processingMethod, instruction, jumpSitess);

                                    // For debug
                                    if (!argsPaths.BeginAtSameInstruction(out _)) {
                                        throw new Exception("Expected 1 args path");
                                    }

                                    var argsPath = argsPaths[0];
                                    var getMethodPointerInstrustion = argsPath.ParametersSources[1].Instructions;

                                    MethodDefinition? pointedMethod;

                                    if (getMethodPointerInstrustion.Length == 1) {
                                        pointedMethod = ((MethodReference)getMethodPointerInstrustion[0].Operand).TryResolve();
                                    }
                                    else if (getMethodPointerInstrustion.Length == 2) {
                                        pointedMethod = ((MethodReference)getMethodPointerInstrustion[1].Operand).TryResolve();
                                    }
                                    else {
                                        throw new Exception("Expected 1 or 2 get method pointer instructions (ldfln or ldvirtfn)");
                                    }

                                    if (pointedMethod is null) {
                                        break;
                                    }

                                    if (trace.TryAddInvocation(pointedMethod)) {
                                        internalChanged = true;
                                    }

                                    if (methodInherits.CheckedMethodImplementationChains.TryGetValue(pointedMethod.GetIdentifier(), out var overrideImplementations)) {
                                        foreach (var overrideImplementation in overrideImplementations) {
                                            if (trace.TryAddInvocation(overrideImplementation)) {
                                                internalChanged = true;
                                            }
                                        }
                                    }
                                }
                                // Delegate from parameters input
                                else {
                                    var paths = MonoModCommon.Stack.AnalyzeParametersSources(processingMethod, instruction, jumpSitess);
                                    var length = paths[0].ParametersSources.Length;
                                    for (var i = 0; i < length; i++) {
                                        var paramIndex = i;

                                        // skip 'this' Parameter load
                                        if (!callingMethod.IsStatic && !isNewObj) {
                                            paramIndex -= 1;
                                        }

                                        if (paramIndex < 0) {
                                            continue;
                                        }

                                        var parameter = callingMethod.Parameters[paramIndex];
                                        var paramType = parameter.ParameterType.TryResolve();
                                        if (paramType is null || !paramType.IsDelegate()) {
                                            continue;
                                        }

                                        List<DelegateInvocationData> combinedFroms = new();

                                        foreach (var argsPath in paths) {

                                            foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(processingMethod, argsPath.ParametersSources[i].Instructions.Last(), jumpSitess)) {
                                                if (delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(processingMethod, loadValue.RealPushValueInstruction), out var combinedFrom)) {
                                                    combinedFroms.Add(combinedFrom);
                                                }
                                            }
                                        }

                                        if (methodInherits.CheckedMethodImplementationChains.TryGetValue(callingMethod.GetIdentifier(), out var invokers)) {
                                            foreach (var invoker in invokers) {
                                                foreach (var inst in invoker.Body.Instructions) {
                                                    if (!MonoModCommon.IL.TryGetReferencedParameter(invoker, inst, out var index, out _)) {
                                                        continue;
                                                    }
                                                    if (invoker.HasThis && invoker.IsConstructor) {
                                                        index -= 1;
                                                    }
                                                    if (index != i) {
                                                        continue;
                                                    }

                                                    if (!delegateTraces.TryGetValue(DelegateInvocationData.GenerateStackKey(invoker, inst), out var data)) {
                                                        data = new DelegateInvocationData(invoker, inst);
                                                        delegateTraces.Add(data.Key, data);
                                                    }

                                                    foreach (var combinedFrom in combinedFroms) {
                                                        if (!data.AddCombinedFrom(combinedFrom)) {
                                                            continue;
                                                        }
                                                        internalChanged = true;
                                                        shouldReloads.TryAdd(invoker.GetIdentifier(), invoker);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                    }
                }
            }
            while (internalChanged);

            requiredReloads = [.. shouldReloads.Values];
        }
    }
}
