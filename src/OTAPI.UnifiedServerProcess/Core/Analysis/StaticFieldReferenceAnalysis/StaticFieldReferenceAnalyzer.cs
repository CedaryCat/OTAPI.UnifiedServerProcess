using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis {
    public sealed class StaticFieldReferenceAnalyzer : Analyzer, IMethodBehaivorFeature {
        public readonly ImmutableDictionary<string, StaticFieldReferenceData> AnalyzedMethods;
        public sealed override string Name => "StaticFieldReferenceAnalyzer";
        readonly TypeInheritanceGraph typeInheritance;
        readonly DelegateInvocationGraph delegateInvocationGraph;
        readonly MethodInheritanceGraph methodInheritanceGraph;
        public DelegateInvocationGraph DelegateInvocationGraph => delegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => methodInheritanceGraph;
        public StaticFieldReferenceAnalyzer(
            ILogger logger,
            ModuleDefinition module,
            TypeInheritanceGraph typeInheritance,
            MethodCallGraph callGraph,
            DelegateInvocationGraph invocationGraph,
            MethodInheritanceGraph methodInherance) : base(logger) {

            this.typeInheritance = typeInheritance;
            this.delegateInvocationGraph = invocationGraph;
            this.methodInheritanceGraph = methodInherance;

            var methodStackTraces = new Dictionary<string, StaticFieldTraceCollection<string>>();
            var methodStaticFieldTraces = new Dictionary<string, StaticFieldTraceCollection<string>>();
            var methodLocalVariableTraces = new Dictionary<string, StaticFieldTraceCollection<VariableDefinition>>();
            var methodReturnTraces = new StaticFieldTraceCollection<string>();

            var workQueue = module.GetAllTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .ToDictionary(m => m.GetIdentifier());

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
                        methodStaticFieldTraces,
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

            AnalyzedMethods = BuildResultDictionary(module, methodStaticFieldTraces, methodLocalVariableTraces, methodReturnTraces, methodStackTraces);
        }

        private static ImmutableDictionary<string, StaticFieldReferenceData> BuildResultDictionary(
            ModuleDefinition module,
            Dictionary<string, StaticFieldTraceCollection<string>> parameterTraces,
            Dictionary<string, StaticFieldTraceCollection<VariableDefinition>> localVariableTraces,
            StaticFieldTraceCollection<string> returnTraces,
            Dictionary<string, StaticFieldTraceCollection<string>> stackTraces) {
            var builder = ImmutableDictionary.CreateBuilder<string, StaticFieldReferenceData>();

            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods.Where(m => m.HasBody)) {
                    string key = method.GetIdentifier();

                    parameterTraces.TryGetValue(key, out var paramTrace);
                    localVariableTraces.TryGetValue(key, out var localTrace);
                    returnTraces.TryGetTrace(key, out var returnTrace);
                    stackTraces.TryGetValue(key, out var stackTrace);

                    if (returnTrace != null || paramTrace?.Count > 0 || localTrace?.Count > 0 || stackTrace?.Count > 0) {
                        builder.Add(key, new StaticFieldReferenceData(
                            method,
                            returnTrace,
                            paramTrace ?? new StaticFieldTraceCollection<string>(),
                            localTrace ?? new StaticFieldTraceCollection<VariableDefinition>(),
                            stackTrace ?? new StaticFieldTraceCollection<string>()
                        ));
                    }
                }
            }

            return builder.ToImmutable();
        }

        private void ProcessMethod(
            ModuleDefinition module,
            MethodDefinition method,
            Dictionary<string, StaticFieldTraceCollection<string>> stackTraces,
            Dictionary<string, StaticFieldTraceCollection<string>> parameterTraces,
            Dictionary<string, StaticFieldTraceCollection<VariableDefinition>> localVariableTraces,
            StaticFieldTraceCollection<string> returnTraces,
            out bool dataChanged) {
            dataChanged = false;

            var hasExternalChange = false;
            if (!method.HasBody) return;

            var methodKey = method.GetIdentifier();

            // initialize data containers
            if (!stackTraces.TryGetValue(methodKey, out var stackTrace)) {
                stackTrace = new StaticFieldTraceCollection<string>();
                stackTraces[methodKey] = stackTrace;
            }

            if (!parameterTraces.TryGetValue(methodKey, out var paramTrace)) {
                paramTrace = new StaticFieldTraceCollection<string>();
                parameterTraces[methodKey] = paramTrace;
            }

            if (!localVariableTraces.TryGetValue(methodKey, out var localTrace)) {
                localTrace = new StaticFieldTraceCollection<VariableDefinition>();
                localVariableTraces[methodKey] = localTrace;
            }

            // build jump source mapping
            var jumpSites = this.GetMethodJumpSites(method);
            bool hasInnerChange;

            do {
                hasInnerChange = false;

                foreach (var instruction in method.Body.Instructions) {
                    switch (instruction.OpCode.Code) {
                        case Code.Ret:
                            HandleReturnInstruction(instruction);
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

                        case Code.Ldsfld:
                            HandleLoadStaticField(instruction);
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

                    if (stackTrace.TryGetTrace(StaticFieldReferenceData.GenerateStackKey(method, loadInstr), out var value)) {
                        if (returnTraces.TryAddTrace(method.GetIdentifier(), value)) {
                            hasExternalChange = true;
                        }
                    }
                }
            }

            void HandleLoadStaticField(Instruction instruction) {
                var fieldDef = ((FieldReference)instruction.Operand).TryResolve();
                if (fieldDef is null) return;
                if (fieldDef.FieldType.IsTruelyValueType()) return;

                var stackKey = StaticFieldReferenceData.GenerateStackKey(method, instruction);

                if (stackTrace.TryAddOriginChain(stackKey, new StaticFieldOriginChain(fieldDef, []))) {
                    hasInnerChange = true;
                }
            }

            void HandleStoreLocal(Instruction instruction) {
                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                if (variable.VariableType.IsTruelyValueType()) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadPath in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                        var loadInstr = loadPath.RealPushValueInstruction;
                        var stackKey = StaticFieldReferenceData.GenerateStackKey(method, loadInstr);

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

                // If loading a traced variable to the stack, then we need to update the stack trace
                if (localTrace.TryGetTrace(variable, out var trace)) {
                    var stackKey = StaticFieldReferenceData.GenerateStackKey(method, instruction);
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

                        var instanceKey = StaticFieldReferenceData.GenerateStackKey(method, instanceLoad.RealPushValueInstruction);

                        // When loading instance fields from values containing tracked static fields (or their members):
                        // 1) Verify if the loaded field is the tracked static field itself (as member)
                        // 2) Check if the loaded field is a reference member of the tracked static field (as host)
                        // If either condition holds, the stack value references the tracked static field
                        // Use TryTrackMemberLoad for validation, update stack trace when returns true

                        if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace) && instanceTrace.TryTrackMemberLoad(fieldRef, out var newTrace)) {
                            var stackKey = StaticFieldReferenceData.GenerateStackKey(method, instruction);
                            if (stackTrace.TryAddTrace(stackKey, newTrace)) {
                                hasInnerChange = true;
                            }
                        }
                    }
                }
            }

            void HandleStoreField(Instruction instruction) {
                var fieldRef = (FieldReference)instruction.Operand;
                if (fieldRef.FieldType.IsValueType) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {

                    // Get the instance object and value source
                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                        .First();
                    var instanceType = instancePath.StackTopType?.TryResolve();
                    var instanceInstr = instancePath.RealPushValueInstruction;
                    var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[1].Instructions.Last(), jumpSites)
                        .First().RealPushValueInstruction;

                    var valueKey = StaticFieldReferenceData.GenerateStackKey(method, valueInstr);
                    if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) continue;

                    var instanceKey = StaticFieldReferenceData.GenerateStackKey(method, instanceInstr);

                    CompositeStaticFieldTrace? instanceTrace;

                    // If the instance is an auto-generated enumerator (with special name)
                    if (instanceType is not null && instanceType.IsSpecialName && EnumeratorLayer.IsEnumerator(typeInheritance, instanceType)) {
                        instanceTrace = valueTrace.CreateFromStoreSelfInEnumerator();
                    }
                    // normal case
                    else {
                        CompositeStaticFieldTrace filteredValueTrace;
                        if (stackTrace.TryGetTrace(instanceKey, out var existingInstanceTrace)) {
                            filteredValueTrace = new();
                            foreach (var origin in valueTrace.StaticFieldOrigins) {
                                if (existingInstanceTrace.StaticFieldOrigins.ContainsKey(origin.Key)) {
                                    continue;
                                }
                                filteredValueTrace.StaticFieldOrigins.Add(origin.Key, origin.Value);
                            }
                        }
                        else {
                            filteredValueTrace = valueTrace;
                        }
                        // Create field storage tracking chain
                        instanceTrace = filteredValueTrace.CreateFromStoreSelfAsMember(fieldRef);
                    }

                    if (instanceTrace is null) continue;

                    if (stackTrace.TryAddTrace(instanceKey, instanceTrace)) {
                        hasInnerChange = true;
                    }

                    // The 'this' parameter behaves like a return value
                    // so we need to update the return trace
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

            void HandleLoadArrayElement(Instruction instruction, bool isLoadReference) {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                        HandleLoadArrayElementInstrustions(instruction, path.ParametersSources[0].Instructions.Last(), isLoadReference);
                    }
                }
                // To support multi-dim array, they isn't use ldelem instruction but call
                else {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                        HandleLoadArrayElementInstrustions(instruction, path.ParametersSources[0].Instructions.Last(), isLoadReference);
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

            void HandleLoadArrayElementInstrustions(Instruction loadEleInstruction, Instruction loadInstanceInstruction, bool isLoadReference) {
                foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadInstanceInstruction, jumpSites)) {

                    if (loadInstance.StackTopType is not ArrayType arrayType) {
                        continue;
                    }

                    if (!isLoadReference && arrayType.ElementType.IsTruelyValueType()) {
                        continue;
                    }

                    var stackKey = StaticFieldReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                    if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                        baseTrace.TryTrackArrayElementLoad((ArrayType)loadInstance.StackTopType, out var newTrace)) {
                        var targetKey = StaticFieldReferenceData.GenerateStackKey(method, loadEleInstruction);
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

                // Skip index 'path.ParametersSources[1]'

                // Get the value
                var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadValueInstruction, jumpSites)
                    .First().RealPushValueInstruction;

                var valueKey = StaticFieldReferenceData.GenerateStackKey(method, valueInstr);
                if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) return;

                var instanceKey = StaticFieldReferenceData.GenerateStackKey(method, instanceInstr);

                CompositeStaticFieldTrace filteredValueTrace;
                if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                    filteredValueTrace = new();
                    foreach (var origin in valueTrace.StaticFieldOrigins) {
                        if (instanceTrace.StaticFieldOrigins.ContainsKey(origin.Key)) {
                            continue;
                        }
                        filteredValueTrace.StaticFieldOrigins.Add(origin.Key, origin.Value);
                    }
                }
                else {
                    filteredValueTrace = valueTrace;
                }

                // Create element storage tracking chain
                var fieldTrace = filteredValueTrace.CreateFromStoreSelfAsArrayElement((ArrayType)instancePath.StackTopType);

                if (stackTrace.TryAddTrace(instanceKey, fieldTrace)) {
                    hasInnerChange = true;
                }
            }

            void HandleLoadCollectionElement(Instruction instruction) {
                var callingMethod = (MemberReference)instruction.Operand;
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                        if (loadInstance.StackTopType is null) {
                            continue;
                        }

                        var stackKey = StaticFieldReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                        if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                            baseTrace.TryTrackCollectionElementLoad(loadInstance.StackTopType, MonoModCommon.Stack.GetPushType(instruction, method, jumpSites)!, out var newTrace)) {
                            var targetKey = StaticFieldReferenceData.GenerateStackKey(method, instruction);
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

                    var valueKey = StaticFieldReferenceData.GenerateStackKey(method, valueInstr);
                    if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) return;

                    var instanceKey = StaticFieldReferenceData.GenerateStackKey(method, instanceInstr);

                    CompositeStaticFieldTrace filteredValueTrace;
                    if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                        filteredValueTrace = new();
                        foreach (var origin in valueTrace.StaticFieldOrigins) {
                            if (instanceTrace.StaticFieldOrigins.ContainsKey(origin.Key)) {
                                continue;
                            }
                            filteredValueTrace.StaticFieldOrigins.Add(origin.Key, origin.Value);
                        }
                    }
                    else {
                        filteredValueTrace = valueTrace;
                    }

                    // Create element storage tracking chain
                    var fieldTrace = filteredValueTrace.CreateFromStoreSelfAsCollectionElement(instancePath.StackTopType, valuePath.StackTopType);

                    if (stackTrace.TryAddTrace(instanceKey, fieldTrace)) {
                        hasInnerChange = true;
                    }
                }
            }

            void HandleMethodCall(Instruction instruction) {
                var isNewObj = instruction.OpCode == OpCodes.Newobj;
                var methodRef = (MethodReference)instruction.Operand;
                var resolvedCallee = methodRef.TryResolve();

                if (resolvedCallee is null) {
                    // Multi-dim array does not exist a method definition that can be resolved
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

                        if (!stackTrace.TryGetTrace(StaticFieldReferenceData.GenerateStackKey(method, instanceInstr), out var instanceTrace)) {
                            continue;
                        }
                        var enumerator = instanceTrace.CreateFromStoreSelfInEnumerator();
                        if (enumerator is null) {
                            continue;
                        }
                        if (stackTrace.TryAddTrace(StaticFieldReferenceData.GenerateStackKey(method, instruction), enumerator)) {
                            hasInnerChange = true;
                        }
                    }
                    return;
                }

                if (EnumeratorLayer.IsGetCurrentMethod(typeInheritance, method, instruction)) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                        foreach (var instanceLoad in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                            var instanceKey = StaticFieldReferenceData.GenerateStackKey(method, instanceLoad.RealPushValueInstruction);

                            if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace) && instanceTrace.TryTrackEnumeratorCurrent(out var newTrace)) {
                                var stackKey = StaticFieldReferenceData.GenerateStackKey(method, instruction);
                                if (stackTrace.TryAddTrace(stackKey, newTrace)) {
                                    hasInnerChange = true;
                                }
                            }
                        }
                    }
                    return;
                }

                if (CollectionElementLayer.IsStoreElementMethod(typeInheritance, method, instruction, out var indexOfValueInParameters)) {
                    // try-get is instance method, the argument index is indexOfValueInParameters + 1
                    HandleStoreCollectionElement(instruction, indexOfValueInParameters + 1);
                    return;
                }

                // Handle collection element load
                if (CollectionElementLayer.IsLoadElementMethod(typeInheritance, method, instruction, out var tryGetOutParamIndex)
                    // If its not a try-get method, element is just push on the stack like normal
                    && tryGetOutParamIndex == -1) {
                    // Then we can handle this like normal load
                    HandleLoadCollectionElement(instruction);
                    return;
                }

                // Get all implementations of the called method
                var implementations = this.GetMethodImplementations(method, instruction, jumpSites, out _);

                // Analyze the parameters of the called method
                var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites);
                MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                    var path = paramPaths[i];
                    loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                    for (int j = 0; j < path.ParametersSources.Length; j++) {
                        loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSites).First();
                    }
                }

                foreach (var implCallee in implementations) {

                    // Static field directly from the called method
                    if (returnTraces.TryGetTrace(implCallee.GetIdentifier(), out var calleeReturnTrace)) {
                        if (stackTrace.TryAddTrace(StaticFieldReferenceData.GenerateStackKey(method, instruction), calleeReturnTrace)) {
                            hasExternalChange = true;
                        }
                    }

                    // Static field from the parameters
                    foreach (var paramGroup in loadParamsInEveryPaths) {
                        for (int paramIndex = 0; paramIndex < paramGroup.Length; paramIndex++) {

                            var paramIndexInImpl = paramIndex;
                            // There are also delegate invocations in implementations
                            // so we use original 'resolvedMethod' instead 'implMethod'
                            // because the implMethod may be a static method,
                            // but the original method is a delegate's invoke method which is never static
                            if (!resolvedCallee.IsStatic && !isNewObj) {
                                paramIndexInImpl -= 1;
                            }
                            // paramIndexInImpl == -1 means the resolvedMethod is a instance method
                            // but the implMethod is a static method
                            // so implMethod must be a static method in delegate invocations
                            if (paramIndexInImpl == -1 && implCallee.IsStatic) {
                                continue;
                            }

                            if (resolvedCallee.Name == nameof(Action.BeginInvoke)
                                && resolvedCallee.DeclaringType.IsDelegate()
                                && (paramIndex == paramGroup.Length - 1 || paramIndex == paramGroup.Length - 2)) {
                                continue;
                            }

                            var paramInImpl = paramIndexInImpl == -1 ? implCallee.Body.ThisParameter : implCallee.Parameters[paramIndexInImpl];

                            if (paramIndexInImpl != -1 && paramInImpl.ParameterType.IsTruelyValueType()) {
                                continue;
                            }

                            var loadValue = paramGroup[paramIndex];
                            var loadValueKey = StaticFieldReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction);

                            // Means the calling method is a collection try-get method
                            if (tryGetOutParamIndex != -1) {
                                // and because of try-get is instance method, the argument index is tryGetOutParamIndex + 1
                                tryGetOutParamIndex += 1;

                                // The first parameter is the instance, but its not reference any static field
                                // so we can skip
                                if (!stackTrace.TryGetTrace(StaticFieldReferenceData.GenerateStackKey(method, paramGroup[0].RealPushValueInstruction), out var instanceTrace)) {
                                    continue;
                                }

                                var loadInstance = paramGroup[0];
                                if (loadInstance.StackTopType is null || loadValue.StackTopType is null) {
                                    continue;
                                }

                                if (!instanceTrace.TryTrackCollectionElementLoad(loadInstance.StackTopType, loadValue.StackTopType, out var elementAccess)) {
                                    continue;
                                }

                                stackTrace.TryAddTrace(loadValueKey, elementAccess);
                            }

                            if (!stackTrace.TryGetTrace(loadValueKey, out var loadingParamStackValue)) {
                                continue;
                            }

                            // Return value reference propagation
                            if (calleeReturnTrace is not null) {
                                if (!calleeReturnTrace.StaticFieldOrigins.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                    continue;
                                }

                                foreach (var outerPart in loadingParamStackValue.StaticFieldOrigins.Values.SelectMany(v => v.StaticFieldOrigins).ToArray()) {
                                    foreach (var innerPart in paramParts.StaticFieldOrigins) {
                                        var substitution = new StaticFieldOriginChain(outerPart.SourceStaticField, [.. outerPart.MemberAccessChain, .. innerPart.MemberAccessChain]);
                                        if (stackTrace.TryAddOriginChain(StaticFieldReferenceData.GenerateStackKey(method, instruction), substitution)) {
                                            hasExternalChange = true;
                                        }
                                    }
                                }
                            }

                            // Called method reference propagation
                            if (parameterTraces.TryGetValue(implCallee.GetIdentifier(), out var calleeStaticFieldValues)) {
                                foreach (var paramValue in calleeStaticFieldValues) {
                                    if (!paramValue.StaticFieldOrigins.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                        continue;
                                    }

                                    foreach (var outerPart in loadingParamStackValue.StaticFieldOrigins.Values.SelectMany(v => v.StaticFieldOrigins).ToArray()) {
                                        foreach (var innerPart in paramParts.StaticFieldOrigins) {

                                            // Ignore self
                                            if (innerPart.SourceStaticField.Name == paramInImpl.Name) {
                                                continue;
                                            }

                                            var substitution = new StaticFieldOriginChain(outerPart.SourceStaticField, [.. outerPart.MemberAccessChain, .. innerPart.MemberAccessChain]);
                                            if (stackTrace.TryAddOriginChain(loadValueKey, substitution)) {
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
