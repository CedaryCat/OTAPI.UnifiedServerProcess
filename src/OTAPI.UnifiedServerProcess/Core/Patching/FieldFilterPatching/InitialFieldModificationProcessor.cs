using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class InitialFieldModificationProcessor(ILogger logger, AnalyzerGroups analyzers) : LoggedComponent(logger), IFieldFilterArgProcessor, IJumpSitesCacheFeature, IMethodImplementationFeature, IMethodCheckCacheFeature
    {
        public DelegateInvocationGraph DelegateInvocationGraph => analyzers.DelegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => analyzers.MethodInheritanceGraph;
        public MethodCallGraph MethodCallGraph => analyzers.MethodCallGraph;
        public override string Name => nameof(InitialFieldModificationProcessor);

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {

            Dictionary<string, Dictionary<string, List<MethodDefinition>>> methodsCallStacks = [];
            Dictionary<string, int> multipleCalls = [];
            AnalyizeOrPatch(source, patch: false, methodsCallStacks, multipleCalls);
            methodsCallStacks.Clear();
            multipleCalls.Clear();
            AnalyizeOrPatch(source, patch: true, methodsCallStacks, multipleCalls);
        }

        private void AnalyizeOrPatch(FilterArgumentSource source, bool patch, Dictionary<string, Dictionary<string, List<MethodDefinition>>> methodsCallStacks, Dictionary<string, int> multipleCalls) {

            var globalInitializer = source.MainModule.GetType(Constants.GlobalInitializerTypeName);
            var initializerAttribute = source.MainModule.GetType(Constants.InitializerAttributeTypeName);

            var main_PostContentLoadInitialize = source.MainModule.GetType("Terraria.Main").Method("PostContentLoadInitialize");
            var main_LoadPlayers = source.MainModule.GetType("Terraria.Main").Method("LoadPlayers");

            var workQueue = new Stack<MethodDefinition>(source.InitialMethods);
            var visited = new Dictionary<string, MethodDefinition>() {
                {main_PostContentLoadInitialize.GetIdentifier(), main_PostContentLoadInitialize},
                {main_LoadPlayers.GetIdentifier(), main_LoadPlayers},
            };

            while (workQueue.TryPop(out var method)) {
                var mid = method.GetIdentifier();
                if (!visited.TryAdd(mid, method)) {
                    continue;
                }

                if (!methodsCallStacks.TryGetValue(mid, out var callStacks)) {
                    callStacks = new() {
                        { method.GetDebugName(), [method] },
                    };
                }

                var firstCallPath = callStacks.First();

                ProcessMethod(
                    method,
                    source,
                    patch,
                    globalInitializer,
                    initializerAttribute,
                    firstCallPath.Value,
                    multipleCalls,
                    out var addedCallees
                );

                foreach (var callee in addedCallees) {
                    var calleeID = callee.GetIdentifier();
                    if (visited.ContainsKey(calleeID)) {
                        continue;
                    }

                    var calleeCallStack = firstCallPath.Key + " → " + callee.GetDebugName();
                    if (!methodsCallStacks.TryGetValue(calleeID, out var calleeCallPaths)) {
                        methodsCallStacks.Add(calleeID, calleeCallPaths = []);
                    }

                    calleeCallPaths.Add(calleeCallStack, [.. firstCallPath.Value, callee]);

                    workQueue.Push(callee);
                }
            }
        }

        private void ProcessMethod(
            MethodDefinition method,
            FilterArgumentSource source,
            bool patch,
            TypeDefinition globalInitializer,
            TypeDefinition initializerAttribute,
            List<MethodDefinition> callStack,
            Dictionary<string, int> multipleCalls,
            out MethodDefinition[] callees) {

            Dictionary<string, MethodDefinition> myCalingMethods = [];
            callees = [];

            if (!method.HasBody) {
                return;
            }

            if (MethodCallGraph.MediatedCallGraph.TryGetValue(method.GetIdentifier(), out var calls)) {
                foreach (var useds in calls.UsedMethods) {
                    foreach (var call in useds.ImplementedMethods()) {
                        myCalingMethods.TryAdd(call.GetIdentifier(), call);
                    }
                }
            }

            var staticFieldReferenceAnalyzer = analyzers.StaticFieldReferenceAnalyzer;
            staticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(method.GetIdentifier(), out var staticFieldReferenceData);
            var typeInheritanceGraph = analyzers.TypeInheritanceGraph;
            var paramModificationAnalyzer = analyzers.ParamModificationAnalyzer;

            var jumpSites = this.GetMethodJumpSites(method);

            Dictionary<string, HashSet<Instruction>> fieldModificationInstructions = [];
            Dictionary<string, HashSet<Instruction>> fieldReferenceInstructions = [];

            void AddFieldModification(MethodDefinition method, Dictionary<string, HashSet<Instruction>> dict, FieldDefinition field, Instruction instruction) {
                var id = field.GetIdentifier();
                if (!dict.TryGetValue(id, out var instructions)) {
                    dict.Add(id, instructions = []);
                }

                HashSet<Instruction> tmp = [];

                if (MonoModCommon.Stack.GetPopCount(method.Body, instruction) > 0) {
                    ExtractSources(this, method, tmp, instruction);
                }
                foreach (var inst in tmp) {
                    instructions.Add(inst);
                }

                if (MonoModCommon.Stack.GetPushCount(method.Body, instruction) > 0) {
                    TraceUsage(this, method, tmp, instruction);
                }
                foreach (var inst in tmp) {
                    instructions.Add(inst);
                }
            }

            foreach (var instruction in method.Body.Instructions) {

                switch (instruction.OpCode.Code) {
                    case Code.Stfld: {
                            var field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                continue;
                            }
                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                                var loadModifyingInstance = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)
                                    .First();

                                if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(method, loadModifyingInstance.RealPushValueInstruction), out var stackValueTrace)) {
                                    continue;
                                }

                                foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                    foreach (var part in willBeModified.PartTracingPaths) {
                                        if (part.EncapsulationHierarchy.Length == 0) {
                                            if (!source.InitialStaticFields.ContainsKey(willBeModified.TracingStaticField.GetIdentifier())) {
                                                continue;
                                            }
                                            AddFieldModification(
                                                method,
                                                fieldModificationInstructions,
                                                willBeModified.TracingStaticField,
                                                instruction);
                                        }
                                    }
                                }
                            }

                            break;
                        }
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj: {

                            var isNewObj = instruction.OpCode == OpCodes.Newobj;
                            var calleeRef = (MethodReference)instruction.Operand;
                            var resolvedCallee = calleeRef.TryResolve();

                            var calleeId = calleeRef.GetIdentifier();
                            multipleCalls.TryAdd(calleeId, 0);
                            multipleCalls[calleeId]++;

                            // Get all implementations of the called method
                            var implementations = this.GetMethodImplementations(method, instruction, jumpSites, out _);

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

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

                            if (
                                // Multidimensional array does not exist a method definition that can be resolved
                                (resolvedCallee is null && calleeRef.DeclaringType is ArrayType arrayType && calleeRef.Name is "Address" or "Set")

                                // The called method is a collection method
                                || (resolvedCallee is not null && CollectionElementLayer.IsModificationMethod(typeInheritanceGraph, method, instruction))

                                // The called method return a reference
                                || (calleeRef.ReturnType is ByReferenceType && calleeRef.Name is "get_Item")) {

                                foreach (var paramGroup in loadParamsInEveryPaths) {
                                    var loadInstance = paramGroup[0];

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(method, loadInstance.RealPushValueInstruction), out var stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        if (!source.InitialStaticFields.ContainsKey(willBeModified.TracingStaticField.GetIdentifier())) {
                                            continue;
                                        }
                                        AddFieldModification(
                                            method,
                                            fieldModificationInstructions,
                                            willBeModified.TracingStaticField,
                                            instruction);
                                    }
                                }

                                continue;
                            }

                            if (resolvedCallee is null) {
                                continue;
                            }

                            foreach (var implCallee in implementations) {

                                foreach (var paramGroup in loadParamsInEveryPaths) {
                                    for (int paramIndex = 0; paramIndex < paramGroup.Length; paramIndex++) {

                                        var paramIndexInImpl = paramIndex;
                                        // There are also delegate invocations in implementations
                                        // so we use original 'resolvedCallee' instead 'implCallee'
                                        // because the implCallee may be a static method,
                                        // but the original method is a delegate's invoke method which is never static
                                        if (!resolvedCallee.IsStatic && !isNewObj) {
                                            paramIndexInImpl -= 1;
                                        }
                                        // paramIndexInImpl == -1 means the resolvedCallee is a instance method
                                        // but the implCallee is a static method
                                        // so implCallee must be a static method in delegate invocations
                                        if (paramIndexInImpl == -1 && implCallee.IsStatic) {
                                            continue;
                                        }

                                        if (resolvedCallee.Name == nameof(Action.BeginInvoke)
                                            && resolvedCallee.DeclaringType.IsDelegate()
                                            && (paramIndex == paramGroup.Length - 1 || paramIndex == paramGroup.Length - 2)) {
                                            continue;
                                        }

                                        var loadParam = paramGroup[paramIndex];

                                        // If the callMethod do not modify any Parameter, skip
                                        if (!paramModificationAnalyzer.ModifiedParameters.TryGetValue(implCallee.GetIdentifier(), out var modifiedParameters)) {
                                            continue;
                                        }
                                        // If the input argument is not modified by the callMethod, skip
                                        if (!modifiedParameters.TryGetValue(paramIndex, out var modifiedParameter)) {
                                            continue;
                                        }

                                        // If the input argument is not coming from a static staticField, skip
                                        if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                            StaticFieldUsageTrack.GenerateStackKey(method, loadParam.RealPushValueInstruction),
                                            out var stackValueTrace)) {
                                            continue;
                                        }

                                        foreach (var referencedStaticField in stackValueTrace.TracedStaticFields.Values) {
                                            if (!source.InitialStaticFields.ContainsKey(referencedStaticField.TracingStaticField.GetIdentifier())) {
                                                continue;
                                            }

                                            List<MemberAccessStep[]> chains = [];
                                            foreach (var part in referencedStaticField.PartTracingPaths) {
                                                foreach (var willBeModified in modifiedParameters.Values) {
                                                    foreach (var modification in willBeModified.Mutations) {
                                                        if (part.EncapsulationHierarchy.Length > 0) {
                                                            if (modification.ModificationAccessPath.Length <= part.EncapsulationHierarchy.Length) {
                                                                continue;
                                                            }
                                                            bool isLeadingChain = true;
                                                            for (int i = 0; i < part.ComponentAccessPath.Length; i++) {
                                                                if (modification.ModificationAccessPath[i] != part.EncapsulationHierarchy[i]) {
                                                                    isLeadingChain = false;
                                                                    break;
                                                                }
                                                            }
                                                            if (isLeadingChain) {
                                                                chains.Add([.. part.ComponentAccessPath, .. modification.ModificationAccessPath.Skip(part.EncapsulationHierarchy.Length)]);
                                                            }
                                                        }
                                                        else {
                                                            chains.Add([.. part.ComponentAccessPath, .. modification.ModificationAccessPath]);
                                                        }
                                                    }
                                                }
                                            }

                                            // Chains are for debugging purposes only.
                                            if (chains.Count > 0) {
                                                AddFieldModification(
                                                    method,
                                                    fieldModificationInstructions,
                                                    referencedStaticField.TracingStaticField,
                                                    instruction);
                                                chains.Clear();
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    case Code.Ldsflda: {
                            if (MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
                                break;
                            }
                            goto case Code.Stsfld;
                        }
                    case Code.Stsfld: {
                            var field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                break;
                            }
                            if (!source.InitialStaticFields.ContainsKey(field.GetIdentifier())) {
                                continue;
                            }
                            var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites);
                            AddFieldModification(
                                method,
                                fieldModificationInstructions,
                                field,
                                instruction);
                            break;
                        }
                    case Code.Ldelema: {
                            if (MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
                                break;
                            }
                            goto case Code.Stelem_Any;
                        }

                    case Code.Stelem_Any:
                    case Code.Stelem_I:
                    case Code.Stelem_I1:
                    case Code.Stelem_I2:
                    case Code.Stelem_I4:
                    case Code.Stelem_I8:
                    case Code.Stelem_R4:
                    case Code.Stelem_R8:
                    case Code.Stelem_Ref: {

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            foreach (var callPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                                foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, callPath.ParametersSources[0].Instructions.Last(), jumpSites)) {

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                        StaticFieldUsageTrack.GenerateStackKey(method, loadInstance.RealPushValueInstruction),
                                        out var stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        if (!source.InitialStaticFields.ContainsKey(willBeModified.TracingStaticField.GetIdentifier())) {
                                            continue;
                                        }
                                        foreach (var part in willBeModified.PartTracingPaths) {
                                            if (part.EncapsulationHierarchy.Length == 0) {
                                                AddFieldModification(
                                                    method,
                                                    fieldModificationInstructions,
                                                    willBeModified.TracingStaticField,
                                                    instruction);
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                }
            }

            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap = [];

            var loopBlocks = ExtractLoopBlock(method);
            BuildConditionBranchMaps(method, out var conditionBranchInstructions, out var branchBlockMapToConditions);
            var ignoreExtractLocalModifications = loopBlocks.Keys.ToHashSet();

            foreach (var blockData in loopBlocks.Values) {
                foreach (var inst in blockData.OrigiLoopBody) {
                    if (inst.Operand is MethodReference multipleCall) {
                        multipleCalls[multipleCall.GetIdentifier()] = 999;
                    }
                }
            }

            HashSet<Instruction> extractDestinations = [];

            foreach (var fieldModification in fieldModificationInstructions) {
                var initFieldDef = source.InitialStaticFields[fieldModification.Key];

                ExpandBranchSourcesUntilStable(method, initFieldDef, fieldModification.Value, branchBlockMapToConditions, localMap, ignoreExtractLocalModifications);

                Dictionary<string, FieldDefinition> referencedOtherStaticFields = [];
                foreach (var modificationInst in fieldModification.Value.ToArray()) {
                    if (modificationInst.Operand is FieldReference referecedField) {
                        var field = referecedField.TryResolve();
                        if (field is null) {
                            continue;
                        }
                        if (!field.IsStatic) {
                            continue;
                        }
                        referencedOtherStaticFields.TryAdd(field.GetIdentifier(), field);
                    }
                }

                foreach (var inst in method.Body.Instructions) {
                    if (inst.Operand is not FieldReference checkField
                        || checkField.GetIdentifier() == initFieldDef.GetIdentifier()
                        || !referencedOtherStaticFields.ContainsKey(checkField.GetIdentifier())) {
                        continue;
                    }
                    extractDestinations.Clear();
                    switch (inst.OpCode.Code) {
                        case Code.Ldsfld:
                        case Code.Ldsflda:
                            TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                            break;
                        case Code.Stsfld:
                            ExtractSources(this, method, initFieldDef, extractDestinations, localMap, [inst], ignoreExtractLocalModifications);
                            break;
                    }
                    foreach (var dest in extractDestinations) {
                        fieldModification.Value.Add(dest);
                    }
                }

                HashSet<VariableDefinition> referencedLocals = [];
                foreach (var modificationInst in fieldModification.Value.ToArray()) {
                    if (MonoModCommon.IL.TryGetReferencedVariable(method, modificationInst, out var referencedLocal)) {
                        referencedLocals.Add(referencedLocal);
                    }
                }

                Dictionary<Instruction, LoopBlockData> origiBodyInstToLoopBlock = [];
                foreach (var loopBlock in loopBlocks.Values) {
                    foreach (var inst in loopBlock.OrigiLoopBody) {
                        origiBodyInstToLoopBlock[inst] = loopBlock;
                    }
                }

                foreach (var inst in method.Body.Instructions) {
                    if (origiBodyInstToLoopBlock.TryGetValue(inst, out var loopBlockData)) {

                        var addLoopBody = fieldModification.Value.Contains(inst);

                        if (addLoopBody && inst.OpCode == OpCodes.Dup) {
                            extractDestinations.Clear();
                            ExtractSources(this, method, initFieldDef, extractDestinations, localMap, [inst], ignoreExtractLocalModifications);

                            if (!loopBlockData.FilteredLoopBody.TryGetValue(fieldModification.Key, out var loopBodyInst)) {
                                loopBodyInst = loopBlockData.FilteredLoopBody[fieldModification.Key] = [];
                            }
                            if (extractDestinations.Count > 0) {
                                var fieldId = initFieldDef.GetIdentifier();
                            }
                            continue;
                        }

                        if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var local)) {
                            if (addLoopBody) {
                                if (!loopBlockData.FilteredLoopBody.TryGetValue(fieldModification.Key, out var loopBodyInst)) {
                                    loopBodyInst = loopBlockData.FilteredLoopBody[fieldModification.Key] = [];
                                }
                                loopBodyInst.Add(inst);
                            }
                            continue;
                        }

                        if (addLoopBody && loopBlockData.OriginalLocal.Index == local.Index) {
                            // Unlike the other variables handled below, the scope of the loopForFields variable is very limited,
                            // and it is usually only modified during the iteration part of the loopForFields.
                            // Therefore, we only handle the instruction "push the loopForFields variable onto the stack" that is needed for modifying the static field.

                            var exceptBody = loopBlockData.InstsExceptBody().ToHashSet();
                            if (!MonoModCommon.IL.MatchLoadVariable(method, inst, out _) && !exceptBody.Contains(inst)) {
                                continue;
                            }
                            extractDestinations.Clear();
                            TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                            if (extractDestinations.Count > 0) {
                                if (!loopBlockData.FilteredLoopBody.TryGetValue(fieldModification.Key, out var loopBodyInst)) {
                                    loopBodyInst = loopBlockData.FilteredLoopBody[fieldModification.Key] = [];
                                }
                                foreach (var extracted in extractDestinations) {
                                    loopBodyInst.Add(extracted);
                                }
                            }
                        }
                        else {
                            if (referencedLocals.Contains(local)) {
                                extractDestinations.Clear();
                                switch (inst.OpCode.Code) {
                                    case Code.Stloc_0:
                                    case Code.Stloc_1:
                                    case Code.Stloc_2:
                                    case Code.Stloc_3:
                                    case Code.Stloc_S:
                                    case Code.Stloc:
                                        ExtractSources(this, method, initFieldDef, extractDestinations, localMap, [inst], ignoreExtractLocalModifications);
                                        break;
                                    case Code.Ldloc_0:
                                    case Code.Ldloc_1:
                                    case Code.Ldloc_2:
                                    case Code.Ldloc_3:
                                    case Code.Ldloc_S:
                                    case Code.Ldloc:
                                        if (!local.VariableType.IsTruelyValueType()) {
                                            TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                                        }
                                        break;
                                    case Code.Ldloca_S:
                                    case Code.Ldloca:
                                        TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                                        break;
                                }
                                if (extractDestinations.Count > 0) {
                                    if (!loopBlockData.FilteredLoopBody.TryGetValue(fieldModification.Key, out var loopBodyInst)) {
                                        loopBodyInst = loopBlockData.FilteredLoopBody[fieldModification.Key] = [];
                                    }
                                    foreach (var extracted in extractDestinations) {
                                        loopBodyInst.Add(extracted);
                                    }
                                }
                            }
                        }
                    }
                    else {
                        extractDestinations.Clear();
                        if (fieldModification.Value.Contains(inst) && inst.OpCode == OpCodes.Dup) {
                            ExtractSources(this, method, initFieldDef, extractDestinations, localMap, [inst], ignoreExtractLocalModifications);
                            foreach (var extracted in extractDestinations) {
                                fieldModification.Value.Add(extracted);
                            }
                            continue;
                        }
                        if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var local)) {
                            continue;
                        }
                        // However, for other more common variables, they can be regarded as having the scope of the entire method.
                        // Therefore, we need to trace the Mutations that occur to them within the entire method.
                        // Thus, our processing object becomes all the instructions that operate on this variable.
                        if (referencedLocals.Contains(local)) {
                            switch (inst.OpCode.Code) {
                                case Code.Stloc_0:
                                case Code.Stloc_1:
                                case Code.Stloc_2:
                                case Code.Stloc_3:
                                case Code.Stloc_S:
                                case Code.Stloc:
                                    ExtractSources(this, method, initFieldDef, extractDestinations, localMap, [inst], ignoreExtractLocalModifications);
                                    break;
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                case Code.Ldloc_S:
                                case Code.Ldloc:
                                    if (!local.VariableType.IsTruelyValueType()) {
                                        TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                                    }
                                    break;
                                case Code.Ldloca_S:
                                case Code.Ldloca:
                                    TraceUsage(this, method, initFieldDef, extractDestinations, localMap, inst, ignoreExtractLocalModifications);
                                    break;
                            }
                            foreach (var extracted in extractDestinations) {
                                fieldModification.Value.Add(extracted);
                            }
                        }
                    }
                }

                ExpandBranchSourcesUntilStable(method, initFieldDef, fieldModification.Value, branchBlockMapToConditions, localMap, ignoreExtractLocalModifications);
                foreach (var loopForFields in loopBlocks.Values) {
                    if (loopForFields.FilteredLoopBody.TryGetValue(fieldModification.Key, out var loopBodyInst)) {
                        ExpandBranchSourcesUntilStable(method, initFieldDef, loopBodyInst, branchBlockMapToConditions, localMap, ignoreExtractLocalModifications);
                    }
                }

                extractDestinations.Clear();
                List<Instruction> extractSources = [];
                foreach (var inst in fieldModification.Value) {
                    if (inst.Operand is Instruction jumpTarget) {
                        extractSources.Add(jumpTarget);
                    }
                    else if (inst.Operand is Instruction[] jumpTargets) {
                        foreach (var target in jumpTargets) {
                            extractSources.Add(target);
                        }
                    }
                }
                ExtractSources(this, method, initFieldDef, extractDestinations, localMap, extractSources, ignoreExtractLocalModifications);
                foreach (var dest in extractDestinations) {
                    fieldModification.Value.Add(dest);
                }

                foreach (var inst in method.Body.Instructions) {
                    if (inst.OpCode == OpCodes.Pop || inst.OpCode == OpCodes.Br || inst.OpCode == OpCodes.Br_S) {
                        foreach (var block in loopBlocks.Values) {
                            foreach (var modification in block.FilteredLoopBody) {
                                if (modification.Value.Contains(inst.Previous) && modification.Value.Contains(inst.Next)) {
                                    modification.Value.Add(inst);
                                }
                            }
                        }
                        if (fieldModification.Value.Contains(inst.Previous) && fieldModification.Value.Contains(inst.Next)) {
                            fieldModification.Value.Add(inst);
                        }
                    }
                }
            }

            HashSet<Instruction> extractedStaticInsts = [];

            bool UsedStaticField(MethodDefinition method) {
                HashSet<string> visited = [];
                Stack<MethodDefinition> stack = [];

                stack.Push(method);
                visited.Add(method.GetIdentifier());

                while (stack.Count > 0) {
                    var caller = stack.Pop();

                    foreach (var inst in caller.Body.Instructions) {
                        if (inst.OpCode == OpCodes.Ldsflda || inst.OpCode == OpCodes.Stsfld) {
                            return true;
                        }
                        if (inst.OpCode == OpCodes.Ldsfld) {
                            var field = ((FieldReference)inst.Operand);
                            if (!field.FieldType.IsTruelyValueType()) {
                                return true;
                            }
                        }
                        else if (inst.OpCode == OpCodes.Call) {
                            var resolvedCallee = ((MethodReference)inst.Operand).TryResolve();

                            if (resolvedCallee is null
                                || !resolvedCallee.IsStatic
                                || resolvedCallee.IsConstructor
                                || resolvedCallee.ReturnType.FullName != source.MainModule.TypeSystem.Void.FullName) {
                                continue;
                            }

                            if (!resolvedCallee.HasBody || resolvedCallee.Module.Name != method.Module.Name) {
                                continue;
                            }

                            if (!visited.Add(resolvedCallee.GetIdentifier())) {
                                continue;
                            }

                            stack.Push(resolvedCallee);
                        }
                    }
                }
                return false;
            }

            foreach (var inst in method.Body.Instructions) {

                if (inst.OpCode != OpCodes.Call) {
                    continue;
                }

                var resolvedCallee = ((MethodReference)inst.Operand).TryResolve();
                if (resolvedCallee is null
                    || !resolvedCallee.IsStatic
                    || resolvedCallee.IsConstructor
                    || resolvedCallee.ReturnType.FullName != source.MainModule.TypeSystem.Void.FullName) {
                    continue;
                }

                if (!UsedStaticField(resolvedCallee)) {
                    continue;
                }


                HashSet<Instruction> tmp = [];
                ExtractSources(this, method, tmp, inst);

                if (IsExtractableStaticPart(source, tmp)) {
                    foreach (var staticInst in tmp) {
                        extractedStaticInsts.Add(staticInst);
                    }
                }
            }

            foreach (var loopBlock in loopBlocks.Values) {
                foreach (var fieldModification in loopBlock.FilteredLoopBody) {
                    if (!source.InitialStaticFields.TryGetValue(fieldModification.Key, out var initFieldDef)) {
                        loopBlock.FilteredLoopBody.Remove(fieldModification.Key);
                        continue;
                    }

                    if (!IsExtractableStaticPart(source, initFieldDef, fieldModification.Value)) {
                        loopBlock.FilteredLoopBody.Remove(fieldModification.Key);
                        continue;
                    }

                    foreach (var staticInst in fieldModification.Value) {
                        extractedStaticInsts.Add(staticInst);
                    }
                    continue;
                }
            }

            foreach (var fieldModification in fieldModificationInstructions) {
                if (!source.InitialStaticFields.TryGetValue(fieldModification.Key, out var initFieldDef)) {
                    continue;
                }

                if (!IsExtractableStaticPart(source, initFieldDef, fieldModification.Value)) {
                    continue;
                }

                foreach (var staticInst in fieldModification.Value) {
                    extractedStaticInsts.Add(staticInst);
                }
                continue;
            }

            bool canExtractStaticPart = source.InitialMethods.Contains(method)
                || (
                multipleCalls.TryGetValue(method.GetIdentifier(), out var count)
                && count == 1
                && method.Parameters.Count == 0
                && method.ReturnType.FullName == method.Module.TypeSystem.Void.FullName);

            bool isWholeStaticModification = canExtractStaticPart && method.IsStatic && !method.Body.Instructions.Any(inst =>
                inst.Operand is FieldReference f
                && (f.TryResolve()?.IsStatic ?? true)
                && source.ModifiedStaticFields.ContainsKey(f.GetIdentifier()));

            if (patch && canExtractStaticPart && extractedStaticInsts.Count > 0) {

                MethodDefinition[] origMethodCallChain = [globalInitializer.Method(Constants.GlobalInitializerEntryPointName), .. callStack];
                MethodDefinition[] methodCallChain = new MethodDefinition[origMethodCallChain.Length];
                for (int i = 0; i < origMethodCallChain.Length; i++) {
                    MethodDefinition chainedMethod = origMethodCallChain[i];

                    string generatedCallerName;
                    if (i == 0) {
                        generatedCallerName = chainedMethod.Name;
                        methodCallChain[i] = chainedMethod;
                    }
                    else {
                        var origCaller = origMethodCallChain[i];
                        generatedCallerName = chainedMethod.DeclaringType.Name + "_" + origCaller.Name;
                    }

                    var generatedMethod = globalInitializer.Methods.Where(m => m.Name == generatedCallerName).FirstOrDefault();
                    if (generatedMethod is null) {

                        var module = source.MainModule;
                        generatedMethod = new MethodDefinition(generatedCallerName, Constants.Modifiers.GlobalInitialize, module.TypeSystem.Void);

                        var attr = new CustomAttribute(initializerAttribute.Methods.Single(m => m.IsConstructor && !m.IsStatic));
                        var sysType = new TypeReference(nameof(System), nameof(Type), module, module.TypeSystem.CoreLibrary);
                        attr.ConstructorArguments.Add(new CustomAttributeArgument(sysType, origMethodCallChain[i].DeclaringType));
                        attr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, origMethodCallChain[i].Name));
                        generatedMethod.CustomAttributes.Add(attr);

                        var body = generatedMethod.Body = new MethodBody(generatedMethod);
                        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                        globalInitializer.Methods.Add(generatedMethod);

                        var caller = methodCallChain[i - 1];
                        var ret = caller.Body.Instructions.Last();

                        var call = Instruction.Create(OpCodes.Call, generatedMethod);
                        caller.Body.GetILProcessor().InsertBefore(ret, call);

                        foreach (var (local, fields) in localMap.Values) {
                            generatedMethod.Body.Variables.Add(local);
                        }
                    }

                    methodCallChain[i] = generatedMethod;
                }

                var generated = methodCallChain[^1];
                var returnInst = generated.Body.Instructions.Last();
                var ilProcessor = generated.Body.GetILProcessor();

                if (isWholeStaticModification) {
                    generated.Body.Instructions.Clear();
                    generated.Body.Variables.Clear();
                    generated.Body.Instructions.AddRange(method.Body.Instructions);
                    generated.Body.Variables.AddRange(method.Body.Variables);
                    generated.Body.ExceptionHandlers.AddRange(method.Body.ExceptionHandlers);
                    method.Body.Instructions.Clear();
                    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    method.Body.Variables.Clear();
                    method.Body.ExceptionHandlers.Clear();
                }
                else {
                    Dictionary<Instruction, Instruction> instOrig2GenMap = [];

                    instOrig2GenMap[method.Body.Instructions.Last()] = returnInst;

                    Dictionary<Instruction, List<LoopBlockData>> instToLoopBlocks = [];
                    foreach (var loopBlock in loopBlocks.Values) {
                        foreach (var inst in loopBlock.AllInsts()) {
                            if (!instToLoopBlocks.TryGetValue(inst, out var list)) {
                                list = instToLoopBlocks[inst] = [];
                            }
                            list.Add(loopBlock);
                        }
                    }
                    foreach (var kv in instToLoopBlocks) {
                        var inst = kv.Key;
                        var list = kv.Value;
                        if (list.Count > 1) {

                            var noDup = list.ToHashSet();
                            var priority = noDup.SingleOrDefault(x => x.InitLoopVariable.Contains(inst) || x.LoopCond.Contains(inst) || x.PostLoop.Contains(inst));
                            var sorted = noDup.OrderBy(x => x.OrigiLoopBody.Length);

                            priority ??= sorted.First();

                            list.Clear();
                            list.Add(priority);

                            foreach (var other in sorted) {
                                if (other != priority) {
                                    list.Add(other);
                                }
                            }
                        }
                    }

                    HashSet<Instruction> addedInsts = [];
                    HashSet<Instruction> removedInsts = [];
                    HashSet<LoopBlockData> processedLoops = [];
                    Dictionary<LoopBlockData, HashSet<Instruction>> checkingLoops = [];

                    foreach (var inst in method.Body.Instructions) {
                        static void MapLocal(MethodDefinition method, Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap, Instruction inst, Instruction clone, LoopBlockData? loopBlock) {
                            if (MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var origLocal)) {
                                VariableDefinition local;
                                if (loopBlock is not null && loopBlock.OriginalLocal == origLocal) {
                                    local = loopBlock.Local;
                                }
                                else {
                                    local = localMap[origLocal].local;
                                }
                                switch (clone.OpCode.Code) {
                                    case Code.Ldloc_0:
                                    case Code.Ldloc_1:
                                    case Code.Ldloc_2:
                                    case Code.Ldloc_3:
                                    case Code.Ldloc_S:
                                    case Code.Ldloc:
                                        clone.OpCode = OpCodes.Ldloc;
                                        break;
                                    case Code.Ldloca_S:
                                    case Code.Ldloca:
                                        clone.OpCode = OpCodes.Ldloca;
                                        break;
                                    case Code.Stloc_0:
                                    case Code.Stloc_1:
                                    case Code.Stloc_2:
                                    case Code.Stloc_3:
                                    case Code.Stloc_S:
                                    case Code.Stloc:
                                        clone.OpCode = OpCodes.Stloc;
                                        break;
                                }
                                clone.Operand = local;
                            }
                        }

                        static Instruction CloneAndUpdateMap(Dictionary<Instruction, Instruction> instMap, Instruction inst) {
                            var clone = inst.Clone();
                            clone.Offset = inst.Offset;
                            instMap[inst] = clone;
                            return clone;
                        }

                        if (instToLoopBlocks.TryGetValue(inst, out var loopBlockList)) {

                            foreach (var loopBlock in loopBlockList) {
                                if (loopBlock.FilteredLoopBody.Count == 0 || processedLoops.Contains(loopBlock)) {
                                    continue;
                                }

                                if (!checkingLoops.TryGetValue(loopBlock, out var restInsts)) {

                                    checkingLoops.Add(loopBlock, restInsts = [.. loopBlock.FilteredLoopBody.SelectMany(x => x.Value)]);

                                    generated.Body.Variables.Add(loopBlock.Local);

                                    foreach (var init in loopBlock.InitLoopVariable) {
                                        if (addedInsts.Add(init)) {
                                            var cloneInit = CloneAndUpdateMap(instOrig2GenMap, init);
                                            MapLocal(method, localMap, init, cloneInit, loopBlock);
                                            ilProcessor.InsertBefore(returnInst, cloneInit);
                                        }
                                    }
                                }

                                if (restInsts.Remove(inst)) {
                                    if (addedInsts.Add(inst)) {
                                        removedInsts.Add(inst);
                                        Instruction clone = CloneAndUpdateMap(instOrig2GenMap, inst);
                                        MapLocal(method, localMap, inst, clone, loopBlock);
                                        ilProcessor.InsertBefore(returnInst, clone);
                                    }
                                }

                                if (restInsts.Count == 0) {
                                    foreach (var post in loopBlock.PostLoop) {
                                        if (addedInsts.Add(post)) {
                                            var clonePost = CloneAndUpdateMap(instOrig2GenMap, post);
                                            MapLocal(method, localMap, post, clonePost, loopBlock);
                                            ilProcessor.InsertBefore(returnInst, clonePost);
                                        }
                                    }
                                    foreach (var cond in loopBlock.LoopCond) {
                                        if (addedInsts.Add(cond)) {
                                            var cloneCond = CloneAndUpdateMap(instOrig2GenMap, cond);
                                            MapLocal(method, localMap, cond, cloneCond, loopBlock);
                                            ilProcessor.InsertBefore(returnInst, cloneCond);
                                        }
                                    }
                                    var mapped = (Instruction)(instOrig2GenMap[loopBlock.LoopCond.Last()].Operand = instOrig2GenMap[loopBlock.JumpToLoopHead].Next);
                                    instOrig2GenMap[mapped] = mapped;

                                    checkingLoops.Remove(loopBlock);
                                    processedLoops.Add(loopBlock);
                                }
                            }
                        }
                        else if (addedInsts.Add(inst) && extractedStaticInsts.Contains(inst)) {
                            removedInsts.Add(inst);
                            Instruction clone = CloneAndUpdateMap(instOrig2GenMap, inst);
                            MapLocal(method, localMap, inst, clone, null);
                            ilProcessor.InsertBefore(returnInst, clone);
                        }
                    }

                    foreach (var inst in generated.Body.Instructions) {
                        if (inst.Operand is Instruction jumpTarget) {
                            inst.Operand = instOrig2GenMap[jumpTarget];
                        }
                        else if (inst.Operand is Instruction[] jumpTargets) {
                            for (int i = 0; i < jumpTargets.Length; i++) {
                                jumpTargets[i] = instOrig2GenMap[jumpTargets[i]];
                            }
                        }
                    }

                    foreach (var inst in removedInsts) {
                        method.Body.RemoveInstructionSeamlessly(jumpSites, inst);
                    }
                }
            }

            foreach (var calleeKV in myCalingMethods.ToArray()) {
                var key = calleeKV.Key;
                if (calleeKV.Value.Parameters.Count != 0) {
                    myCalingMethods.Remove(key);
                }
                if (multipleCalls.TryGetValue(key, out var multipleCallCount) && multipleCallCount != 1) {
                    myCalingMethods.Remove(key);
                }
            }

            callees = [.. myCalingMethods.Values];
        }

        private void ExpandBranchSourcesUntilStable(MethodDefinition method,
            FieldDefinition staticField,
            HashSet<Instruction> modifications,
            Dictionary<Instruction, HashSet<Instruction>> branchBlockMapToConditions,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            HashSet<VariableDefinition> ignoreExtractLocalModifications) {

            HashSet<Instruction> extractDestinations = [];
            bool incremented;
            do {
                incremented = false;
                foreach (var inst in modifications.ToArray()) {
                    if (branchBlockMapToConditions.TryGetValue(inst, out var conditions)) {
                        extractDestinations.Clear();
                        ExtractSources(this, method, staticField, extractDestinations, localMap, conditions, ignoreExtractLocalModifications);
                        foreach (var extracted in extractDestinations) {
                            if (modifications.Add(extracted)) {
                                incremented = true;
                            }
                        }
                    }
                }
            }
            while (incremented);
        }

        private static void BuildConditionBranchMaps(MethodDefinition method, out Dictionary<Instruction, HashSet<Instruction>> conditionBranchInstructions, out Dictionary<Instruction, HashSet<Instruction>> branchBlockMapToConditions) {
            conditionBranchInstructions = [];
            branchBlockMapToConditions = [];

            Dictionary<Instruction, (Instruction next, HashSet<Instruction> block)> currentProcessing = [];
            foreach (var instruction in method.Body.Instructions) {
                foreach (var currentKV in currentProcessing.ToArray()) {
                    if (currentKV.Value.next == instruction) {
                        conditionBranchInstructions[currentKV.Key] = currentKV.Value.block;
                        currentProcessing.Remove(currentKV.Key);
                    }
                    else {
                        currentKV.Value.block.Add(instruction);
                    }
                }
                if (instruction.Operand is Instruction jumpTarget && jumpTarget.Offset > instruction.Offset && MonoModCommon.Stack.GetPopCount(method.Body, instruction) > 0) {
                    currentProcessing.Add(instruction, (jumpTarget, []));
                }
            }

            if (currentProcessing.Count > 0) {
                throw new InvalidOperationException();
            }

            foreach (var condBranch in conditionBranchInstructions) {
                foreach (var inst in condBranch.Value) {
                    if (!branchBlockMapToConditions.TryGetValue(inst, out var conditions)) {
                        conditions = branchBlockMapToConditions[inst] = [];
                    }
                    conditions.Add(condBranch.Key);
                }
            }
        }

        private bool IsExtractableStaticPart(FilterArgumentSource source, FieldDefinition initFieldDef, IEnumerable<Instruction> instructions) {
            var fieldId = initFieldDef.GetIdentifier();
            foreach (var inst in instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                    case Code.Ldsflda:
                    case Code.Stsfld: {
                            var fieldRef = (FieldReference)inst.Operand;
                            var id = fieldRef.GetIdentifier();
                            if (source.ModifiedStaticFields.ContainsKey(id)) {
                                source.InitialStaticFields.Remove(fieldId);
                                source.UnmodifiedStaticFields.Remove(fieldId);
                                source.ModifiedStaticFields.TryAdd(fieldId, initFieldDef);
                                return false;
                            }
                        }
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj: {
                            var methodRef = (MethodReference)inst.Operand;
                            var methodDef = methodRef.TryResolve();
                            if (methodDef is null) {
                                break;
                            }
                            if (this.CheckUsedContextBoundField(source.ModifiedStaticFields, methodDef)) {
                                source.InitialStaticFields.Remove(fieldId);
                                source.UnmodifiedStaticFields.Remove(fieldId);
                                source.ModifiedStaticFields.TryAdd(fieldId, initFieldDef);
                                return false;
                            }
                        }
                        break;
                }
            }
            foreach (var inst in instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                    case Code.Ldarg_S:
                    case Code.Ldarg:
                    case Code.Ldarga_S:
                    case Code.Ldarga:
                    case Code.Starg_S:
                    case Code.Starg:
                        return false;
                }
            }
            return true;
        }
        private bool IsExtractableStaticPart(FilterArgumentSource source, IEnumerable<Instruction> instructions) {
            foreach (var inst in instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                    case Code.Ldsflda:
                    case Code.Stsfld: {
                            var fieldRef = (FieldReference)inst.Operand;
                            var id = fieldRef.GetIdentifier();
                            if (source.ModifiedStaticFields.ContainsKey(id)) {
                                return false;
                            }
                        }
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj: {
                            var methodRef = (MethodReference)inst.Operand;
                            var methodDef = methodRef.TryResolve();
                            if (methodDef is null) {
                                break;
                            }
                            if (this.CheckUsedContextBoundField(source.ModifiedStaticFields, methodDef)) {
                                return false;
                            }
                        }
                        break;
                }
            }
            foreach (var inst in instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                    case Code.Ldarg_S:
                    case Code.Ldarg:
                    case Code.Ldarga_S:
                    case Code.Ldarga:
                    case Code.Starg_S:
                    case Code.Starg:
                        return false;
                }
            }
            return true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="OriginalLocal"></param>
        /// <param name="Local"></param>
        /// <param name="InitLoopVariable"></param>
        /// <param name="PostLoop"></param>
        /// <param name="LoopCond"></param>
        /// <param name="FilteredLoopBody">key is field identifier</param>
        /// <param name="OrigiLoopBody"></param>
        record LoopBlockData(VariableDefinition OriginalLocal, VariableDefinition Local,
            Instruction[] InitLoopVariable,
            Instruction JumpToLoopHead,
            Instruction[] PostLoop,
            Instruction[] LoopCond,
            Instruction[] OrigiLoopBody,
            Dictionary<string, HashSet<Instruction>> FilteredLoopBody)
        {

            public IEnumerable<Instruction> AllInsts() => InitLoopVariable.Concat(PostLoop).Concat(LoopCond).Concat(FilteredLoopBody.Values.SelectMany(x => x));
            public IEnumerable<Instruction> SortedAllInsts() => AllInsts().OrderBy(x => x.Offset);
            public IEnumerable<Instruction> InstsExceptBody() => InitLoopVariable.Concat(PostLoop).Concat(LoopCond);

            public readonly HashSet<Instruction> OrigiLoopBodySet = [.. OrigiLoopBody];
        }

        private Dictionary<VariableDefinition, LoopBlockData> ExtractLoopBlock(MethodDefinition method) {
            Dictionary<VariableDefinition, LoopBlockData> loopBlocks = [];

            foreach (var initLoopVariable in method.Body.Instructions) {
                if (!MonoModCommon.IL.MatchSetVariable(method, initLoopVariable, out var loopVariable)) {
                    continue;
                }
                if (initLoopVariable.Next.OpCode != OpCodes.Br && initLoopVariable.Next.OpCode != OpCodes.Br_S) {
                    continue;
                }
                var loopConditionBegin = ((Instruction)initLoopVariable.Next.Operand);
                if (!MonoModCommon.IL.MatchLoadVariable(method, loopConditionBegin, out var checkLoopVariable)
                    || checkLoopVariable != loopVariable) {
                    continue;
                }
                var loopConditionEnd = MonoModCommon.Stack.AnalyzeStackTopValueFinalUsage(method, loopConditionBegin);
                if (loopConditionEnd.Length != 1
                    || loopConditionEnd[0].Operand is not Instruction loopBodyBegin) {
                    continue;
                }

                if (loopBodyBegin != initLoopVariable.Next.Next) {
                    continue;
                }

                var checkConditionPaths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, loopConditionEnd[0], this.GetMethodJumpSites(method));
                if (checkConditionPaths.Length != 1
                    || checkConditionPaths[0].ParametersSources.Length != 2
                    || checkConditionPaths[0].ParametersSources[0].Instructions.Length != 1) {
                    continue;
                }
                switch (checkConditionPaths[0].ParametersSources[1].Instructions[0].OpCode.Code) {
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
                    case Code.Ldc_I4_S:
                    case Code.Ldc_I4:
                    case Code.Ldc_I8:
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                    case Code.Ldsfld:
                        break;
                    default:
                        continue;
                }
                // For loopForFields: for (int i = aaa; i < bbb; i += ccc)

                // int i = xxx
                HashSet<Instruction> extractedLoopVariableInit = [];
                ExtractSources(this, method, extractedLoopVariableInit, initLoopVariable);
                extractedLoopVariableInit.Add(initLoopVariable.Next);

                // i < bbb
                HashSet<Instruction> extractedPostLoop = [];
                ExtractSources(this, method, extractedPostLoop, loopConditionBegin.Previous);

                // i += ccc
                HashSet<Instruction> extractedConditionCheck = [];
                ExtractSources(this, method, extractedConditionCheck, loopConditionEnd[0]);

                var upBound = loopBodyBegin;
                var downBound = loopConditionBegin.Previous;
                var current = upBound;

                List<Instruction> loopBody = [];
                while (current.Offset < downBound.Offset) {
                    if (!extractedLoopVariableInit.Contains(current)
                        && !extractedPostLoop.Contains(current)
                        && !extractedConditionCheck.Contains(current)) {
                        loopBody.Add(current);
                    }
                    current = current.Next;
                }

                loopBlocks.Add(loopVariable,
                    new LoopBlockData(
                        loopVariable,
                        new VariableDefinition(loopVariable.VariableType),
                        [.. extractedLoopVariableInit.OrderBy(x => x.Offset)],
                        initLoopVariable.Next,
                        [.. extractedPostLoop.OrderBy(x => x.Offset)],
                        [.. extractedConditionCheck.OrderBy(x => x.Offset)],
                        [.. loopBody],
                        []));
            }
            return loopBlocks;
        }
        static void TraceUsage(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            HashSet<Instruction> collected,
            Instruction inst) {

            Stack<Instruction> works = [];
            works.Push(inst);

            while (works.Count > 0) {
                var current = works.Pop();
                var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, current);
                ExtractSources(feature, caller, collected, usages);
                foreach (var usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(caller.Body, usage) > 0) {
                        works.Push(usage);
                    }
                }
            }
        }
        static void ExtractSources(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            HashSet<Instruction> collected,
            params IEnumerable<Instruction> extractSources) {

            var jumpSite = feature.GetMethodJumpSites(caller);

            Stack<Instruction> stack = [];
            foreach (var checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                var check = stack.Pop();
                if (!collected.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only pop value from stack
                else if (MonoModCommon.Stack.GetPopCount(caller.Body, check) > 0) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
            }
        }
        static void TraceUsage(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            FieldDefinition referencedField,
            HashSet<Instruction> transformInsts,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            Instruction inst,
            HashSet<VariableDefinition>? ignoreExtractLocalModifications = null) {

            Stack<Instruction> works = [];
            works.Push(inst);

            while (works.Count > 0) {
                var current = works.Pop();
                var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, current);
                ExtractSources(feature, caller, referencedField, transformInsts, localMap, usages, ignoreExtractLocalModifications);
                foreach (var usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(caller.Body, usage) > 0) {
                        works.Push(usage);
                    }
                }
            }
        }
        static void ExtractSources(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            FieldDefinition referenceField,
            HashSet<Instruction> transformInsts,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            IEnumerable<Instruction> extractSources,
            HashSet<VariableDefinition>? ignoreExtractLocalModifications = null) {

            var jumpSite = feature.GetMethodJumpSites(caller);

            Stack<Instruction> stack = [];
            foreach (var checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                var check = stack.Pop();
                if (!transformInsts.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                else if (MonoModCommon.Stack.GetPopCount(caller.Body, check) > 0) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }

                if (MonoModCommon.IL.TryGetReferencedVariable(caller, check, out var local)) {
                    if (ignoreExtractLocalModifications is not null && ignoreExtractLocalModifications.Contains(local)) {
                        transformInsts.Add(check);
                        continue;
                    }

                    if (!localMap.TryGetValue(local, out var tuple)) {
                        localMap.Add(local, tuple = (new VariableDefinition(local.VariableType), []));
                    }
                    if (!tuple.fields.TryAdd(referenceField.GetIdentifier(), referenceField)) {
                        continue;
                    }
                    foreach (var inst in caller.Body.Instructions) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(caller, inst, out var otherLocal) || otherLocal.Index != local.Index) {
                            continue;
                        }
                        switch (inst.OpCode.Code) {
                            case Code.Stloc_0:
                            case Code.Stloc_1:
                            case Code.Stloc_2:
                            case Code.Stloc_3:
                            case Code.Stloc_S:
                            case Code.Stloc:
                                stack.Push(inst);
                                break;
                            case Code.Ldloc_0:
                            case Code.Ldloc_1:
                            case Code.Ldloc_2:
                            case Code.Ldloc_3:
                            case Code.Ldloc_S:
                            case Code.Ldloc:
                                if (!local.VariableType.IsTruelyValueType()) {
                                    foreach (var usage in MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, inst)) {
                                        stack.Push(usage);
                                    }
                                }
                                transformInsts.Add(inst);
                                break;
                            case Code.Ldloca_S:
                            case Code.Ldloca:
                                foreach (var usage in MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, inst)) {
                                    stack.Push(usage);
                                }
                                transformInsts.Add(inst);
                                break;
                        }
                    }
                }
            }
        }
    }
}
