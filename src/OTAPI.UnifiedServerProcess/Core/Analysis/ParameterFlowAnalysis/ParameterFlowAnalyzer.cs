using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{

    public sealed class ParameterFlowAnalyzer : Analyzer, IMethodBehaivorFeature
    {
        public readonly ImmutableDictionary<string, ParameterReferenceData> AnalyzedMethods;
        public sealed override string Name => "ParamFlowAnalyzer";

        readonly DelegateInvocationGraph delegateInvocationGraph;
        readonly MethodInheritanceGraph methodInheritanceGraph;
        public DelegateInvocationGraph DelegateInvocationGraph => delegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => methodInheritanceGraph;

        readonly TypeInheritanceGraph typeInheritance;

        public ParameterFlowAnalyzer(
            ILogger logger,
            ModuleDefinition module,
            TypeInheritanceGraph typeInheritance,
            MethodCallGraph callGraph,
            DelegateInvocationGraph invocationGraph,
            MethodInheritanceGraph inheritanceGraph) : base(logger) {

            this.delegateInvocationGraph = invocationGraph;
            this.methodInheritanceGraph = inheritanceGraph;
            this.typeInheritance = typeInheritance;

            var methodStackTraces = new Dictionary<string, ParameterTraceCollection<string>>();
            var methodParameterTraces = new Dictionary<string, ParameterTraceCollection<string>>();
            var methodLocalVariableTraces = new Dictionary<string, ParameterTraceCollection<VariableDefinition>>();
            var methodReturnTraces = new ParameterTraceCollection<string>();

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
                        method,
                        methodStackTraces,
                        methodParameterTraces,
                        methodLocalVariableTraces,
                        methodReturnTraces,
                        out bool dataChanged
                    );
                    workQueue.Remove(method.GetIdentifier());

                    if (dataChanged && callGraph.MediatedCallGraph.TryGetValue(method.GetIdentifier(), out var callers)) {
                        foreach (var caller in callers.UsedByMethods) {
                            if (workQueue.TryAdd(caller.GetIdentifier(), caller)) {
                                Progress(iteration, progress, currentWorkBatch.Length, "Add: {0}", indent: 1, caller.GetDebugName());
                            }
                        }
                    }
                }
            }

            AnalyzedMethods = BuildResultDictionary(module, methodParameterTraces, methodLocalVariableTraces, methodReturnTraces, methodStackTraces);
        }

        private static ImmutableDictionary<string, ParameterReferenceData> BuildResultDictionary(
            ModuleDefinition module,
            Dictionary<string, ParameterTraceCollection<string>> parameterTraces,
            Dictionary<string, ParameterTraceCollection<VariableDefinition>> localVariableTraces,
            ParameterTraceCollection<string> returnTraces,
            Dictionary<string, ParameterTraceCollection<string>> stackTraces) {
            var builder = ImmutableDictionary.CreateBuilder<string, ParameterReferenceData>();

            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods.Where(m => m.HasBody)) {
                    string key = method.GetIdentifier();

                    parameterTraces.TryGetValue(key, out var paramTrace);
                    localVariableTraces.TryGetValue(key, out var localTrace);
                    returnTraces.TryGetTrace(key, out var returnTrace);
                    stackTraces.TryGetValue(key, out var stackTrace);

                    if (returnTrace != null || paramTrace?.Count > 0 || localTrace?.Count > 0 || stackTrace?.Count > 0) {
                        builder.Add(key, new ParameterReferenceData(
                            method,
                            returnTrace,
                            paramTrace ?? new ParameterTraceCollection<string>(),
                            localTrace ?? new ParameterTraceCollection<VariableDefinition>(),
                            stackTrace ?? new ParameterTraceCollection<string>()
                        ));
                    }
                }
            }

            return builder.ToImmutable();
        }

        private void ProcessMethod(
            ModuleDefinition module,
            MethodDefinition method,
            Dictionary<string, ParameterTraceCollection<string>> stackTraces,
            Dictionary<string, ParameterTraceCollection<string>> parameterTraces,
            Dictionary<string, ParameterTraceCollection<VariableDefinition>> localVariableTraces,
            ParameterTraceCollection<string> returnTraces,
            out bool dataChanged) {
            dataChanged = false;

            var hasExternalChange = false;
            if (!method.HasBody) return;

            var methodKey = method.GetIdentifier();

            if (!stackTraces.TryGetValue(methodKey, out var stackTrace)) {
                stackTrace = new ParameterTraceCollection<string>();
                stackTraces[methodKey] = stackTrace;
            }

            if (!parameterTraces.TryGetValue(methodKey, out var paramTrace)) {
                paramTrace = new ParameterTraceCollection<string>();
                parameterTraces[methodKey] = paramTrace;
            }

            if (!localVariableTraces.TryGetValue(methodKey, out var localTrace)) {
                localTrace = new ParameterTraceCollection<VariableDefinition>();
                localVariableTraces[methodKey] = localTrace;
            }

            // Build jump sites table
            var jumpSites = this.GetMethodJumpSites(method);
            bool hasInnerChange;

            do {
                hasInnerChange = false;

                foreach (var instruction in method.Body.Instructions) {
                    switch (instruction.OpCode.Code) {
                        case Code.Ret:
                            HandleReturnInstruction(instruction);
                            break;

                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                        case Code.Ldarg_S:
                        case Code.Ldarg:
                            HandleLoadArgument(instruction);
                            break;

                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                            HandleStoreLocal(instruction);
                            break;

                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                        case Code.Ldloc_S:
                            HandleLoadLocal(instruction);
                            break;

                        case Code.Ldfld:
                            HandleLoadField(instruction);
                            break;

                        case Code.Stfld:
                            HandleStoreField(instruction);
                            break;

                        case Code.Ldelem_Any:
                            HandleLoadArrayElement(instruction, false);
                            break;

                        case Code.Ldelema:
                        case Code.Ldelem_Ref:
                            HandleLoadArrayElement(instruction, true);
                            break;

                        case Code.Stelem_Ref:
                        case Code.Stelem_Any:
                            HandleStoreArrayElement(instruction);
                            break;

                        case Code.Call:
                        case Code.Callvirt:
                        case Code.Newobj:
                            HandleMethodCall(instruction);
                            break;
                    }
                }

            } while (hasInnerChange);

            dataChanged = hasExternalChange;

            #region Instruction Handlers
            void HandleReturnInstruction(Instruction instruction) {
                if (method.ReturnType.FullName == module.TypeSystem.Void.FullName) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    var loadInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                        .First().RealPushValueInstruction;

                    if (stackTrace.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadInstr), out var value)) {
                        if (returnTraces.TryAddTrace(method.GetIdentifier(), value)) {
                            hasExternalChange = true;
                        }
                    }
                }
            }

            void HandleLoadArgument(Instruction instruction) {
                var parameter = MonoModCommon.IL.GetReferencedParameter(method, instruction);
                if (parameter.ParameterType.IsTruelyValueType()) return;

                var chain = new ParameterTrackingChain(parameter, [], []);
                var stackKey = ParameterReferenceData.GenerateStackKey(method, instruction);

                if (stackTrace.TryAddOriginChain(stackKey, chain)) {
                    hasInnerChange = true;
                }
            }

            void HandleStoreLocal(Instruction instruction) {
                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                if (variable.VariableType.IsTruelyValueType()) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadPath in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                        var loadInstr = loadPath.RealPushValueInstruction;
                        var stackKey = ParameterReferenceData.GenerateStackKey(method, loadInstr);

                        if (stackTrace.TryGetTrace(stackKey, out var trace)) {
                            if (localTrace.TryAddTrace(variable, trace)) {
                                hasInnerChange = true;
                            }
                        }
                    }
                }
            }

            void HandleLoadLocal(Instruction instruction) {
                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                if (variable.VariableType.IsTruelyValueType()) return;

                if (localTrace.TryGetTrace(variable, out var trace)) {
                    var stackKey = ParameterReferenceData.GenerateStackKey(method, instruction);
                    if (stackTrace.TryAddTrace(stackKey, trace)) {
                        hasInnerChange = true;
                    }
                }
            }

            void HandleLoadField(Instruction instruction) {
                var fieldRef = (FieldReference)instruction.Operand;
                if (fieldRef.FieldType.IsTruelyValueType()) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var instanceLoad in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                        var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceLoad.RealPushValueInstruction);

                        if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace) && instanceTrace.TryExtendTrackingWithMemberAccess(fieldRef, out var newTrace)) {
                            var stackKey = ParameterReferenceData.GenerateStackKey(method, instruction);
                            if (stackTrace.TryAddTrace(stackKey, newTrace)) {
                                hasInnerChange = true;
                            }
                        }
                    }
                }
            }

            void HandleStoreField(Instruction instruction) {
                var fieldRef = (FieldReference)instruction.Operand;
                if (fieldRef.FieldType.IsTruelyValueType()) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {

                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                        .First();
                    var instanceInstr = instancePath.RealPushValueInstruction;
                    var instanceType = instancePath.StackTopType?.TryResolve();
                    var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[1].Instructions.Last(), jumpSites)
                        .First().RealPushValueInstruction;

                    var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) continue;

                    var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);

                    CompositeParameterTracking? instanceTrace;

                    // If the instance is an auto-generated enumerator (with special name)
                    if (instanceType is not null && instanceType.IsSpecialName && EnumeratorLayer.IsEnumerator(typeInheritance, instanceType)) {
                        instanceTrace = valueTrace.CreateEncapsulatedEnumeratorInstance();
                    }
                    // normal case
                    else {
                        CompositeParameterTracking filteredValueTrace;
                        if (stackTrace.TryGetTrace(instanceKey, out var existingInstanceTrace)) {
                            filteredValueTrace = new();
                            foreach (var origin in valueTrace.ReferencedParameters) {
                                if (existingInstanceTrace.ReferencedParameters.ContainsKey(origin.Key)) {
                                    continue;
                                }
                                filteredValueTrace.ReferencedParameters.Add(origin.Key, origin.Value);
                            }
                        }
                        else {
                            filteredValueTrace = valueTrace;
                        }
                        // Create field storage tracking tail
                        instanceTrace = filteredValueTrace.CreateEncapsulatedInstance(fieldRef);
                    }

                    if (instanceTrace is null) continue;

                    if (stackTrace.TryAddTrace(instanceKey, instanceTrace)) {
                        hasInnerChange = true;
                    }

                    if (MonoModCommon.IL.TryGetReferencedParameter(method, instanceInstr, out var param)
                        && param.IsParameterThis(method)
                        && method.IsConstructor) {

                        if (returnTraces.TryAddTrace(methodKey, instanceTrace)) {
                            hasExternalChange = true;
                        }
                    }
                    else if (param != null) {
                        if (paramTrace.TryAddTrace(param.Name, instanceTrace)) {
                            hasExternalChange = true;
                        }
                    }
                }
            }

            void HandleLoadArrayElement(Instruction instruction, bool isLoadAddress) {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                        HandleLoadArrayElementInstrustion(instruction, path.ParametersSources[0].Instructions.Last(), isLoadAddress);
                    }
                }
                // To support multi-dim array, they isn't use ldelem instruction but call
                else {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                        HandleLoadArrayElementInstrustion(instruction, path.ParametersSources[0].Instructions.Last(), isLoadAddress);
                    }
                }
            }

            void HandleStoreArrayElement(Instruction instruction) {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                        HandleStoreArrayElementInstrustions(
                            instruction,
                            path.ParametersSources.First().Instructions.Last(),
                            path.ParametersSources.Last().Instructions.Last());
                    }
                }
                // To support multi-dim array, they isn't use stelem instruction but call
                else {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                        HandleStoreArrayElementInstrustions(
                            instruction,
                            path.ParametersSources.First().Instructions.Last(),
                            path.ParametersSources.Last().Instructions.Last());
                    }
                }
            }

            void HandleLoadArrayElementInstrustion(Instruction loadEleInstruction, Instruction loadInstanceInstruction, bool isLoadAddress) {
                foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadInstanceInstruction, jumpSites)) {

                    if (loadInstance.StackTopType is not ArrayType arrayType) {
                        continue;
                    }

                    if (!isLoadAddress && arrayType.ElementType.IsTruelyValueType()) {
                        continue;
                    }

                    var stackKey = ParameterReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                    if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                        baseTrace.TryExtendTrackingWithArrayAccess(arrayType, out var newTrace)) {
                        var targetKey = ParameterReferenceData.GenerateStackKey(method, loadEleInstruction);
                        if (stackTrace.TryAddTrace(targetKey, newTrace)) {
                            hasInnerChange = true;
                        }
                    }
                }
            }

            void HandleStoreArrayElementInstrustions(Instruction storeInstruction, Instruction loadInstanceInstruction, Instruction loadValueInstruction) {
                // Get the instance object
                var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadInstanceInstruction, jumpSites)
                    .First();
                if (instancePath.StackTopType is null)
                    return;
                var instanceInstr = instancePath.RealPushValueInstruction;

                // Get the value
                var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadValueInstruction, jumpSites)
                    .First().RealPushValueInstruction;

                var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                var valueTraceIsNull = !stackTrace.TryGetTrace(valueKey, out var valueTrace);
                valueTrace ??= new CompositeParameterTracking();
                var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);
                var instanceTraceIsNull = !stackTrace.TryGetTrace(instanceKey, out var instanceTrace);
                instanceTrace ??= new CompositeParameterTracking();
                bool instanceTraceHasChange = false;
                bool valueTraceHasChange = false;

                foreach (var valueSingleParamTraceKV in valueTrace.ReferencedParameters) {
                    if (!instanceTrace.ReferencedParameters.TryGetValue(valueSingleParamTraceKV.Key, out var instanceSingleParamTrace)) {
                        instanceSingleParamTrace = new(valueSingleParamTraceKV.Value.TrackedParameter, []);
                    }
                    foreach (var chain in valueSingleParamTraceKV.Value.PartTrackingPaths) {
                        if (instanceSingleParamTrace.PartTrackingPaths.Add(chain.CreateEncapsulatedArrayInstance((ArrayType)instancePath.StackTopType))) {
                            hasInnerChange = true;
                            instanceTraceHasChange = true;
                        }
                    }
                    instanceTrace.ReferencedParameters[valueSingleParamTraceKV.Key] = instanceSingleParamTrace;
                }
                foreach (var instanceSingleParamTraceKV in instanceTrace.ReferencedParameters) {
                    if (valueTrace.ReferencedParameters.TryGetValue(instanceSingleParamTraceKV.Key, out var valueSingleParamTrace)) {
                        continue;
                    }
                    valueSingleParamTrace = new(instanceSingleParamTraceKV.Value.TrackedParameter, []);
                    foreach (var chain in instanceSingleParamTraceKV.Value.PartTrackingPaths) {
                        if (chain.TryExtendTrackingWithArrayAccess((ArrayType)instancePath.StackTopType, out var newChain)
                            && valueSingleParamTrace.PartTrackingPaths.Add(newChain)) {
                            hasInnerChange = true;
                            valueTraceHasChange = true;
                        }
                    }
                    valueTrace.ReferencedParameters[instanceSingleParamTraceKV.Key] = valueSingleParamTrace;
                }

                if (valueTraceIsNull && valueTraceHasChange) {
                    stackTrace.TryAddTrace(valueKey, valueTrace);
                }
                if (instanceTraceIsNull && instanceTraceHasChange) {
                    stackTrace.TryAddTrace(instanceKey, instanceTrace);
                }

                //if (stackTrace.TryGetTrace(valueKey, out var valueTrace)) {
                //    CompositeParameterTracking filteredValueTrace;

                //    if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                //        filteredValueTrace = new();
                //        foreach (var origin in valueTrace.ReferencedParameters) {
                //            if (instanceTrace.ReferencedParameters.ContainsKey(origin.Key)) {
                //                continue;
                //            }
                //            filteredValueTrace.ReferencedParameters.Add(origin.Key, origin.Value);
                //        }
                //    }
                //    else {
                //        filteredValueTrace = valueTrace;
                //    }

                //    // Create element storage tracking tail
                //    var traceData = filteredValueTrace.CreateEncapsulatedArrayInstance((ArrayType)instancePath.StackTopType);

                //    if (stackTrace.TryAddTrace(instanceKey, traceData)) {
                //        hasInnerChange = true;
                //    }
                //}
            }

            void HandleLoadCollectionElement(Instruction instruction) {
                var callingMethod = (MemberReference)instruction.Operand;
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                        if (loadInstance.StackTopType is null) {
                            continue;
                        }

                        var stackKey = ParameterReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                        if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                            baseTrace.TryExtendTrackingWithCollectionAccess(loadInstance.StackTopType, MonoModCommon.Stack.GetPushType(instruction, method, jumpSites)!, out var newTrace)) {
                            var targetKey = ParameterReferenceData.GenerateStackKey(method, instruction);
                            if (stackTrace.TryAddTrace(targetKey, newTrace)) {
                                hasInnerChange = true;
                            }
                        }
                    }
                }
            }

            void HandleStoreCollectionElement(Instruction instruction, int indexOfValueInArguments) {
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                        .First();
                    if (instancePath.StackTopType is null)
                        return;
                    var instanceInstr = instancePath.RealPushValueInstruction;
                    var valuePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[indexOfValueInArguments].Instructions.Last(), jumpSites)
                        .First();
                    if (valuePath.StackTopType is null)
                        return;
                    var valueInstr = valuePath.RealPushValueInstruction;

                    var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    var valueTraceIsNull = !stackTrace.TryGetTrace(valueKey, out var valueTrace);
                    valueTrace ??= new CompositeParameterTracking();
                    var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);
                    var instanceTraceIsNull = !stackTrace.TryGetTrace(instanceKey, out var instanceTrace);
                    instanceTrace ??= new CompositeParameterTracking();
                    bool instanceTraceHasChange = false;
                    bool valueTraceHasChange = false;

                    foreach (var valueSingleParamTraceKV in valueTrace.ReferencedParameters) {
                        if (instanceTrace.ReferencedParameters.TryGetValue(valueSingleParamTraceKV.Key, out var instanceSingleParamTrace)) {
                            continue;
                        }
                        instanceSingleParamTrace = new(valueSingleParamTraceKV.Value.TrackedParameter, []);
                        foreach (var chain in valueSingleParamTraceKV.Value.PartTrackingPaths) {
                            if (instanceSingleParamTrace.PartTrackingPaths.Add(chain.CreateEncapsulatedCollectionInstance(instancePath.StackTopType, valuePath.StackTopType))) {
                                hasInnerChange = true;
                                instanceTraceHasChange = true;
                            }
                        }
                        instanceTrace.ReferencedParameters[valueSingleParamTraceKV.Key] = instanceSingleParamTrace;
                    }

                    foreach (var instanceSingleParamTraceKV in instanceTrace.ReferencedParameters) {
                        if (!valueTrace.ReferencedParameters.TryGetValue(instanceSingleParamTraceKV.Key, out var valueSingleParamTrace)) {
                            valueSingleParamTrace = new(instanceSingleParamTraceKV.Value.TrackedParameter, []);
                        }
                        foreach (var chain in instanceSingleParamTraceKV.Value.PartTrackingPaths) {
                            if (chain.TryExtendTrackingWithCollectionAccess(instancePath.StackTopType, valuePath.StackTopType, out var newChain)
                                && valueSingleParamTrace.PartTrackingPaths.Add(newChain)) {
                                hasInnerChange = true;
                                valueTraceHasChange = true;
                            }
                        }
                        valueTrace.ReferencedParameters[instanceSingleParamTraceKV.Key] = valueSingleParamTrace;
                    }

                    if (valueTraceIsNull && valueTraceHasChange) {
                        stackTrace.TryAddTrace(valueKey, valueTrace);
                    }
                    if (instanceTraceIsNull && instanceTraceHasChange) {
                        stackTrace.TryAddTrace(instanceKey, instanceTrace);
                    }

                    //var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    //if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) return;

                    //var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);

                    //CompositeParameterTracking filteredValueTrace;
                    //if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                    //    filteredValueTrace = new();
                    //    foreach (var origin in valueTrace.ReferencedParameters) {
                    //        if (instanceTrace.ReferencedParameters.ContainsKey(origin.Key)) {
                    //            continue;
                    //        }
                    //        filteredValueTrace.ReferencedParameters.Add(origin.Key, origin.Value);
                    //    }
                    //}
                    //else {
                    //    filteredValueTrace = valueTrace;
                    //}

                    //// Create element storage tracking tail
                    //var fieldTrace = filteredValueTrace.CreateEncapsulatedCollectionInstance(instancePath.StackTopType, valuesPath.StackTopType);

                    //if (stackTrace.TryAddTrace(instanceKey, fieldTrace)) {
                    //    hasInnerChange = true;
                    //}
                }
            }
            void HandleStoreCollectionElements(Instruction instruction) {
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                        .First();
                    if (instancePath.StackTopType is null)
                        return;
                    var instanceInstr = instancePath.RealPushValueInstruction; 
                    var valuesPath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[1].Instructions.Last(), jumpSites)
                        .First();
                    if (valuesPath.StackTopType is null)
                        return;
                    var valueInstr = valuesPath.RealPushValueInstruction;

                    var valueType = valuesPath.StackTopType;
                    var valueElementType = valueType is ArrayType arrayType ? arrayType.ElementType : ((GenericInstanceType)valueType).GenericArguments.Last();

                    var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    var valueTraceIsNull = !stackTrace.TryGetTrace(valueKey, out var valueTrace);
                    valueTrace ??= new CompositeParameterTracking();
                    var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);
                    var instanceTraceIsNull = !stackTrace.TryGetTrace(instanceKey, out var instanceTrace);
                    instanceTrace ??= new CompositeParameterTracking();
                    bool instanceTraceHasChange = false;
                    bool valueTraceHasChange = false;

                    foreach (var valueSingleParamTraceKV in valueTrace.ReferencedParameters) {
                        if (instanceTrace.ReferencedParameters.TryGetValue(valueSingleParamTraceKV.Key, out var instanceSingleParamTrace)) {
                            continue;
                        }
                        instanceSingleParamTrace = new(valueSingleParamTraceKV.Value.TrackedParameter, []);
                        foreach (var valueChain in valueSingleParamTraceKV.Value.PartTrackingPaths) {
                            if (valueChain.TryExtendTrackingWithCollectionAccess(valueType, valueElementType, out var elementChain)
                                && instanceSingleParamTrace.PartTrackingPaths.Add(elementChain.CreateEncapsulatedCollectionInstance(instancePath.StackTopType, valueElementType))) {
                                hasInnerChange = true;
                                instanceTraceHasChange = true;
                            }
                        }
                        instanceTrace.ReferencedParameters[valueSingleParamTraceKV.Key] = instanceSingleParamTrace;
                    }

                    foreach (var instanceSingleParamTraceKV in instanceTrace.ReferencedParameters) {
                        if (!valueTrace.ReferencedParameters.TryGetValue(instanceSingleParamTraceKV.Key, out var valueSingleParamTrace)) {
                            valueSingleParamTrace = new(instanceSingleParamTraceKV.Value.TrackedParameter, []);
                        }
                        foreach (var containChain in instanceSingleParamTraceKV.Value.PartTrackingPaths) {
                            if (containChain.TryExtendTrackingWithCollectionAccess(valueType, valueElementType, out var innerChain)
                                && valueSingleParamTrace.PartTrackingPaths.Add(innerChain.CreateEncapsulatedCollectionInstance(instancePath.StackTopType, valueElementType))) {
                                hasInnerChange = true;
                                valueTraceHasChange = true;
                            }
                        }
                        valueTrace.ReferencedParameters[instanceSingleParamTraceKV.Key] = valueSingleParamTrace;
                    }

                    if (valueTraceIsNull && valueTraceHasChange) {
                        stackTrace.TryAddTrace(valueKey, valueTrace);
                    }
                    if (instanceTraceIsNull && instanceTraceHasChange) {
                        stackTrace.TryAddTrace(instanceKey, instanceTrace);
                    }
                }
            }

            void HandleMethodCall(Instruction instruction) {
                bool isNewObj = instruction.OpCode == OpCodes.Newobj;
                var methodRef = (MethodReference)instruction.Operand;
                var resolvedMethod = methodRef.TryResolve();


                if (resolvedMethod is null) {
                    // Multidimensional array does not exist a method definition that can be resolved
                    if (methodRef.DeclaringType is ArrayType arrayType) {
                        if (methodRef.Name == "Get" && !arrayType.ElementType.IsTruelyValueType()) {
                            HandleLoadArrayElement(instruction, true);
                        }
                        else if (methodRef.Name == "Address") {
                            HandleLoadArrayElement(instruction, true);
                        }
                        else if (methodRef.Name == "Set") {
                            HandleStoreArrayElement(instruction);
                        }
                    }
                    return;
                }

                if (EnumeratorLayer.IsGetEnumeratorMethod(typeInheritance, method, instruction)) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                        var instanceLoad = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                            .First();
                        var instanceType = instanceLoad.StackTopType?.TryResolve();
                        var instanceInstr = instanceLoad.RealPushValueInstruction;

                        if (!stackTrace.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, instanceInstr), out var instanceTrace)) {
                            continue;
                        }
                        var enumerator = instanceTrace.CreateEncapsulatedEnumeratorInstance();
                        if (enumerator is null) {
                            continue;
                        }
                        if (stackTrace.TryAddTrace(ParameterReferenceData.GenerateStackKey(method, instruction), enumerator)) {
                            hasInnerChange = true;
                        }
                    }
                    return;
                }

                if (EnumeratorLayer.IsGetCurrentMethod(typeInheritance, method, instruction)) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                        foreach (var instanceLoad in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                            var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceLoad.RealPushValueInstruction);

                            if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace) && instanceTrace.TryTrackEnumeratorCurrent(out var newTrace)) {
                                var stackKey = ParameterReferenceData.GenerateStackKey(method, instruction);
                                if (stackTrace.TryAddTrace(stackKey, newTrace)) {
                                    hasInnerChange = true;
                                }
                            }
                        }
                    }
                    return;
                }

                if (CollectionElementLayer.IsStoreElementMethod(typeInheritance, method, instruction, out var paramIndexWithoutInstance)) {
                    HandleStoreCollectionElement(instruction, paramIndexWithoutInstance + 1);
                    return;
                }

                if (CollectionElementLayer.IsStoreElementsMethod(typeInheritance, method, instruction)) {
                    HandleStoreCollectionElements(instruction);
                    return;
                }

                // Handle collection element load
                if (CollectionElementLayer.IsLoadElementMethod(typeInheritance, method, instruction, out var tryGetOutParamIndexWithoutInstance)
                    // If its not a try-get method, element is just push on the stack like normal
                    && tryGetOutParamIndexWithoutInstance == -1) {
                    // Then we can handle this like normal load
                    HandleLoadCollectionElement(instruction);
                    return;
                }

                // Skip any pure static function that does not reference any external aruments
                if ((resolvedMethod.IsStatic || isNewObj) && resolvedMethod.Parameters.Count == 0) {
                    return;
                }
                // Get all implementations of the called method
                var implementations = this.GetMethodImplementations(method, instruction, jumpSites, out _);

                // Analyze TrackingParameter sources
                var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites);
                MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                    var path = paramPaths[i];
                    loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                    for (int j = 0; j < path.ParametersSources.Length; j++) {
                        loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSites).First();
                    }
                }

                foreach (var implMethod in implementations) {

                    returnTraces.TryGetTrace(implMethod.GetIdentifier(), out var calleeReturnTrace);

                    foreach (var paramGroup in loadParamsInEveryPaths) {
                        for (int paramIndex = 0; paramIndex < paramGroup.Length; paramIndex++) {

                            var paramIndexInImpl = paramIndex;

                            // There are also delegate invocations in implementations
                            // so we use original 'resolvedMethod' instead 'implMethod'
                            // because the implMethod may be a static method,
                            // but the original method is a delegate's invoke method which is never static
                            if (!resolvedMethod.IsStatic && !isNewObj) {
                                paramIndexInImpl -= 1;
                            }
                            // paramIndexInImpl == -1 means the resolvedMethod is a instance method
                            // but the implMethod is a static method
                            // so implMethod must be a static method in delegate invocations
                            if (paramIndexInImpl == -1 && implMethod.IsStatic) {
                                continue;
                            }

                            if (resolvedMethod.Name == nameof(Action.BeginInvoke)
                                && resolvedMethod.DeclaringType.IsDelegate()
                                && (paramIndex == paramGroup.Length - 1 || paramIndex == paramGroup.Length - 2)) {
                                continue;
                            }

                            var paramInImpl = paramIndexInImpl == -1 ? implMethod.Body.ThisParameter : implMethod.Parameters[paramIndexInImpl];

                            if (paramIndexInImpl != -1 && paramInImpl.ParameterType.IsTruelyValueType()) {
                                continue;
                            }

                            var loadParam = paramGroup[paramIndex];
                            var loadParamKey = StaticFieldReferenceData.GenerateStackKey(method, loadParam.RealPushValueInstruction);

                            // Means the calling method is a collection try-get method
                            if (tryGetOutParamIndexWithoutInstance != -1) {
                                // and because of try-get is instance method, the argument index is tryGetOutParamIndexWithoutInstance + 1
                                var tryGetOutParamIndexWithInstance = tryGetOutParamIndexWithoutInstance + 1;

                                // The first TrackingParameter is the instance, but its not reference any static field
                                // so we can skip
                                if (!stackTrace.TryGetTrace(StaticFieldReferenceData.GenerateStackKey(method, paramGroup[0].RealPushValueInstruction), out var instanceTrace)) {
                                    continue;
                                }

                                var loadInstance = paramGroup[0];
                                if (loadInstance.StackTopType is null || loadParam.StackTopType is null) {
                                    continue;
                                }

                                if (!instanceTrace.TryExtendTrackingWithCollectionAccess(loadInstance.StackTopType, loadParam.StackTopType, out var elementAccess)) {
                                    continue;
                                }

                                stackTrace.TryAddTrace(StaticFieldReferenceData.GenerateStackKey(method, paramGroup[tryGetOutParamIndexWithInstance].RealPushValueInstruction), elementAccess);
                            }

                            if (!stackTrace.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadParam.RealPushValueInstruction), out var loadingParamStackValue)) {
                                continue;
                            }

                            // Return value TrackingParameter propagation
                            if (calleeReturnTrace is not null) {
                                if (!calleeReturnTrace.ReferencedParameters.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                    continue;
                                }

                                foreach (var outerPart in loadingParamStackValue.ReferencedParameters.Values.SelectMany(v => v.PartTrackingPaths).ToArray()) {
                                    foreach (var innerPart in paramParts.PartTrackingPaths) {
                                        var substitution = ParameterTrackingChain.CombineParameterTraces(outerPart, innerPart);
                                        if (substitution is not null && stackTrace.TryAddOriginChain(ParameterReferenceData.GenerateStackKey(method, instruction), substitution)) {
                                            hasExternalChange = true;
                                        }
                                    }
                                }
                            }

                            // Parameter propagation of the caller
                            if (parameterTraces.TryGetValue(implMethod.GetIdentifier(), out var calleeParameterValues)) {
                                foreach (var paramValue in calleeParameterValues) {
                                    if (!paramValue.ReferencedParameters.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                        continue;
                                    }

                                    foreach (var outerPart in loadingParamStackValue.ReferencedParameters.Values.SelectMany(v => v.PartTrackingPaths).ToArray()) {
                                        foreach (var innerPart in paramParts.PartTrackingPaths) {
                                            //// Ignore self
                                            //if (innerPart.TrackingParameter.Name == paramInImpl.Name) {
                                            //    continue;
                                            //}
                                            var substitution = ParameterTrackingChain.CombineParameterTraces(outerPart, innerPart);
                                            if (substitution is not null && stackTrace.TryAddOriginChain(ParameterReferenceData.GenerateStackKey(method, loadParam.RealPushValueInstruction), substitution)) {
                                                hasExternalChange = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            #endregion
        }
    }
}
