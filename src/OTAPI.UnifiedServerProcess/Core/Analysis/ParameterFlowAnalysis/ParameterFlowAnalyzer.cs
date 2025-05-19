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

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {

    public sealed class ParameterFlowAnalyzer : Analyzer, IMethodBehaivorFeature {
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

            // 初始化数据容器
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

            // 构建跳转源映射
            var jumpSitess = this.GetMethodJumpSites(method);
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

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                    var loadInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)
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

                var chain = new ParameterOriginChain(parameter, []);
                var stackKey = ParameterReferenceData.GenerateStackKey(method, instruction);

                if (stackTrace.TryAddOriginChain(stackKey, chain)) {
                    hasInnerChange = true;
                }
            }

            void HandleStoreLocal(Instruction instruction) {
                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                if (variable.VariableType.IsTruelyValueType()) return;

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                    foreach (var loadPath in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)) {
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

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                    foreach (var instanceLoad in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)) {

                        var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceLoad.RealPushValueInstruction);

                        if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace) && instanceTrace.TryTrackMemberLoad(fieldRef, out var newTrace)) {
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

                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {

                    // 获取实例对象和存储值的来源
                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)
                        .First();
                    var instanceInstr = instancePath.RealPushValueInstruction;
                    var instanceType = instancePath.StackTopType?.TryResolve();
                    var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[1].Instructions.Last(), jumpSitess)
                        .First().RealPushValueInstruction;

                    var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) continue;

                    var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);

                    CompositeParameterTrace? instanceTrace;

                    // If the instance is an auto-generated enumerator (with special name)
                    if (instanceType is not null && instanceType.IsSpecialName && EnumeratorLayer.IsEnumerator(typeInheritance, instanceType)) {
                        instanceTrace = valueTrace.CreateFromStoreSelfInEnumerator();
                    }
                    // normal case
                    else {
                        CompositeParameterTrace filteredValueTrace;
                        if (stackTrace.TryGetTrace(instanceKey, out var existingInstanceTrace)) {
                            filteredValueTrace = new();
                            foreach (var origin in valueTrace.ParameterOrigins) {
                                if (existingInstanceTrace.ParameterOrigins.ContainsKey(origin.Key)) {
                                    continue;
                                }
                                filteredValueTrace.ParameterOrigins.Add(origin.Key, origin.Value);
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

                    // 处理构造函数中的this参数
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
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                        HandleLoadArrayElementInstrustion(instruction, path.ParametersSources[0].Instructions.Last(), isLoadAddress);
                    }
                }
                // To support multi-dim array, they isn't use ldelem instruction but call
                else {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSitess)) {
                        HandleLoadArrayElementInstrustion(instruction, path.ParametersSources[0].Instructions.Last(), isLoadAddress);
                    }
                }
            }

            void HandleStoreArrayElement(Instruction instruction) {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                        HandleStoreArrayElementInstrustions(
                            instruction,
                            path.ParametersSources.First().Instructions.Last(),
                            path.ParametersSources.Last().Instructions.Last());
                    }
                }
                // To support multi-dim array, they isn't use stelem instruction but call
                else {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSitess)) {
                        HandleStoreArrayElementInstrustions(
                            instruction,
                            path.ParametersSources.First().Instructions.Last(),
                            path.ParametersSources.Last().Instructions.Last());
                    }
                }
            }

            void HandleLoadArrayElementInstrustion(Instruction loadEleInstruction, Instruction loadInstanceInstruction, bool isLoadAddress) {
                foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadInstanceInstruction, jumpSitess)) {

                    if (loadInstance.StackTopType is not ArrayType arrayType) {
                        continue;
                    }

                    if (!isLoadAddress && arrayType.ElementType.IsTruelyValueType()) {
                        continue;
                    }

                    var stackKey = ParameterReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                    if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                        baseTrace.TryTrackArrayElementLoad(arrayType, out var newTrace)) {
                        var targetKey = ParameterReferenceData.GenerateStackKey(method, loadEleInstruction);
                        if (stackTrace.TryAddTrace(targetKey, newTrace)) {
                            hasInnerChange = true;
                        }
                    }
                }
            }

            void HandleStoreArrayElementInstrustions(Instruction storeInstruction, Instruction loadInstanceInstruction, Instruction loadValueInstruction) {
                // Get the instance object
                var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadInstanceInstruction, jumpSitess)
                    .First();
                if (instancePath.StackTopType is null)
                    return;
                var instanceInstr = instancePath.RealPushValueInstruction;

                // Get the value
                var valueInstr = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, loadValueInstruction, jumpSitess)
                    .First().RealPushValueInstruction;

                var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) return;

                var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);


                CompositeParameterTrace filteredValueTrace;
                if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                    filteredValueTrace = new();
                    foreach (var origin in valueTrace.ParameterOrigins) {
                        if (instanceTrace.ParameterOrigins.ContainsKey(origin.Key)) {
                            continue;
                        }
                        filteredValueTrace.ParameterOrigins.Add(origin.Key, origin.Value);
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
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                    foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)) {
                        if (loadInstance.StackTopType is null) {
                            continue;
                        }

                        var stackKey = ParameterReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction);

                        if (stackTrace.TryGetTrace(stackKey, out var baseTrace) &&
                            baseTrace.TryTrackCollectionElementLoad(loadInstance.StackTopType, MonoModCommon.Stack.GetPushType(instruction, method, jumpSitess)!, out var newTrace)) {
                            var targetKey = ParameterReferenceData.GenerateStackKey(method, instruction);
                            if (stackTrace.TryAddTrace(targetKey, newTrace)) {
                                hasInnerChange = true;
                            }
                        }
                    }
                }
            }

            void HandleStoreCollectionElement(Instruction instruction, int indexOfValueInArguments) {
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSitess)) {
                    var instancePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)
                        .First();
                    if (instancePath.StackTopType is null)
                        return;
                    var instanceInstr = instancePath.RealPushValueInstruction;
                    var valuePath = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[indexOfValueInArguments].Instructions.Last(), jumpSitess)
                        .First();
                    if (valuePath.StackTopType is null)
                        return;
                    var valueInstr = valuePath.RealPushValueInstruction;

                    var valueKey = ParameterReferenceData.GenerateStackKey(method, valueInstr);
                    if (!stackTrace.TryGetTrace(valueKey, out var valueTrace)) return;

                    var instanceKey = ParameterReferenceData.GenerateStackKey(method, instanceInstr);

                    CompositeParameterTrace filteredValueTrace;
                    if (stackTrace.TryGetTrace(instanceKey, out var instanceTrace)) {
                        filteredValueTrace = new();
                        foreach (var origin in valueTrace.ParameterOrigins) {
                            if (instanceTrace.ParameterOrigins.ContainsKey(origin.Key)) {
                                continue;
                            }
                            filteredValueTrace.ParameterOrigins.Add(origin.Key, origin.Value);
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
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSitess)) {
                        var instanceLoad = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)
                            .First();
                        var instanceType = instanceLoad.StackTopType?.TryResolve();
                        var instanceInstr = instanceLoad.RealPushValueInstruction;

                        if (!stackTrace.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, instanceInstr), out var instanceTrace)) {
                            continue;
                        }
                        var enumerator = instanceTrace.CreateFromStoreSelfInEnumerator();
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
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSitess)) {
                        foreach (var instanceLoad in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSitess)) {

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

                // Skip any pure static function that does not reference any external aruments
                if ((resolvedMethod.IsStatic || isNewObj) && resolvedMethod.Parameters.Count == 0) {
                    return;
                }
                // Get all implementations of the called method
                var implementations = this.GetMethodImplementations(method, instruction, jumpSitess, out _);

                // Analyze parameter sources
                var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSitess);
                MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                    var path = paramPaths[i];
                    loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                    for (int j = 0; j < path.ParametersSources.Length; j++) {
                        loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSitess).First();
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

                            var loadValue = paramGroup[paramIndex];
                            if (!stackTrace.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), out var loadingParamStackValue)) {
                                continue;
                            }

                            // Return value parameter propagation
                            if (calleeReturnTrace is not null) {
                                if (!calleeReturnTrace.ParameterOrigins.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                    continue;
                                }

                                foreach (var outerPart in loadingParamStackValue.ParameterOrigins.Values.SelectMany(v => v.ParameterOrigins).ToArray()) {
                                    foreach (var innerPart in paramParts.ParameterOrigins) {
                                        var substitution = new ParameterOriginChain(outerPart.SourceParameter, [.. outerPart.MemberAccessChain, .. innerPart.MemberAccessChain]);
                                        if (stackTrace.TryAddOriginChain(ParameterReferenceData.GenerateStackKey(method, instruction), substitution)) {
                                            hasExternalChange = true;
                                        }
                                    }
                                }
                            }

                            // Parameter propagation of the caller
                            if (parameterTraces.TryGetValue(implMethod.GetIdentifier(), out var calleeParameterValues)) {
                                foreach (var paramValue in calleeParameterValues) {
                                    if (!paramValue.ParameterOrigins.TryGetValue(paramInImpl.Name, out var paramParts)) {
                                        continue;
                                    }

                                    foreach (var outerPart in loadingParamStackValue.ParameterOrigins.Values.SelectMany(v => v.ParameterOrigins).ToArray()) {
                                        foreach (var innerPart in paramParts.ParameterOrigins) {

                                            // Ignore self
                                            if (innerPart.SourceParameter.Name == paramInImpl.Name) {
                                                continue;
                                            }

                                            var substitution = new ParameterOriginChain(outerPart.SourceParameter, [.. outerPart.MemberAccessChain, .. innerPart.MemberAccessChain]);
                                            if (stackTrace.TryAddOriginChain(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), substitution)) {
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
