using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldModificationAnalysis
{
    class FieldModificationState()
    {
        public readonly Stack<MethodDefinition> works = [];
        public readonly HashSet<FieldDefinition> fields = [];
    }
    public class StaticFieldModificationAnalyzer(ILogger logger,
        StaticFieldReferenceAnalyzer staticFieldReferenceAnalyzer,
        ParamModificationAnalyzer paramModificationAnalyzer,
        ParameterFlowAnalyzer parameterFlowAnalyzer,
        MethodCallGraph callGraph,
        DelegateInvocationGraph invocationGraph,
        MethodInheritanceGraph inheritanceGraph,
        TypeInheritanceGraph typeInheritanceGraph) : Analyzer(logger), IMethodImplementationFeature, IMethodCheckCacheFeature
    {
        public DelegateInvocationGraph DelegateInvocationGraph => invocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => inheritanceGraph;
        public MethodCallGraph MethodCallGraph => callGraph;


        public sealed override string Name => "FieldModificationAnalyzer";

        readonly TypeInheritanceGraph typeInheritanceGraph = typeInheritanceGraph;
        readonly MethodCallGraph callGraph = callGraph;
        readonly ParameterFlowAnalyzer parameterFlowAnalyzer = parameterFlowAnalyzer;
        readonly ParamModificationAnalyzer paramModificationAnalyzer = paramModificationAnalyzer;
        readonly StaticFieldReferenceAnalyzer staticFieldReferenceAnalyzer = staticFieldReferenceAnalyzer;
        public void FetchModifiedFields(MethodDefinition[] entryPoint, MethodDefinition[] initOnlys, out FieldDefinition[] modifiedFields, out FieldDefinition[] initModifiedFields) {
            HashSet<string> initOnlyMethodSet = [.. initOnlys.Select(m => m.GetIdentifier())];
            Dictionary<string, FieldDefinition> modifiedFields_ignoredInitOnlys = FetchModifiedFieldInner(entryPoint, initOnlyMethodSet);
            Dictionary<string, FieldDefinition> modifiedFields_WhenInit = FetchModifiedFieldInner(initOnlys, []);

            foreach (KeyValuePair<string, FieldDefinition> fieldKV in modifiedFields_ignoredInitOnlys) {
                modifiedFields_WhenInit.Remove(fieldKV.Key);
            }
            foreach (KeyValuePair<string, FieldDefinition> kv in modifiedFields_WhenInit.ToArray()) {
                if (kv.Value.DeclaringType.Name.OrdinalStartsWith('<')) {
                    modifiedFields_WhenInit.Remove(kv.Key);
                }
            }

            initModifiedFields = [.. modifiedFields_WhenInit.Values];
            modifiedFields = [.. modifiedFields_ignoredInitOnlys.Values];

            return;
        }

        private Dictionary<string, FieldDefinition> FetchModifiedFieldInner(MethodDefinition[] entryPoints, HashSet<string> ignored) {
            Dictionary<string, (MethodDefinition method, string path)> workQueue = entryPoints.ToDictionary(x => x.GetIdentifier(), method => { var path = method.GetDebugName(); return (method, path); });

            var visited = new Dictionary<string, MethodDefinition>();

            var storedFields = new Dictionary<string, FieldDefinition>();

            int iteration = 0;
            while (workQueue.Count > 0) {
                iteration++;
                (MethodDefinition method, string path)[] currentWorkBatch = workQueue.Values.ToArray();

                for (int progress = 0; progress < currentWorkBatch.Length; progress++) {
                    (MethodDefinition? method, string? path) = currentWorkBatch[progress];
                    Progress(iteration, progress, currentWorkBatch.Length, method.GetDebugName());
                    ProcessMethod(
                        method,
                        storedFields,
                        out MethodDefinition[]? addedCallees
                    );

                    var methodId = method.GetIdentifier();
                    workQueue.Remove(methodId);
                    visited.TryAdd(methodId, method);

                    foreach (MethodDefinition callee in addedCallees) {
                        var calleeID = callee.GetIdentifier();
                        if (ignored.Contains(calleeID)) {
                            continue;
                        }
                        if (visited.ContainsKey(calleeID)) {
                            continue;
                        }
                        if (workQueue.TryAdd(calleeID, (callee, path + "→" + callee.GetDebugName()))) {
                            Progress(iteration, progress, currentWorkBatch.Length, "Add: {0}", indent: 1, callee.GetDebugName());
                        }
                    }
                }
            }

            return storedFields;
        }

        private void ProcessMethod(
            MethodDefinition caller,
            Dictionary<string, FieldDefinition> storedFields,
            out MethodDefinition[] callees) {

            callees = [];

            Dictionary<string, MethodDefinition> myCalingMethods = [];

            if (!caller.HasBody) {
                return;
            }

            static void AddField(Dictionary<string, FieldDefinition> dict, FieldDefinition field) {
                dict.TryAdd(field.GetIdentifier(), field);
            }

            static bool IsAtomicModificationMethod(MethodReference callee) {
                if (callee.Parameters.Count == 0 || callee.Parameters[0].ParameterType is not ByReferenceType) {
                    return false;
                }

                if (callee.DeclaringType.FullName == typeof(System.Threading.Volatile).FullName) {
                    return callee.Name is "Write";
                }

                if (callee.DeclaringType.FullName == typeof(System.Threading.Interlocked).FullName) {
                    return callee.Name is "Exchange"
                        or "CompareExchange"
                        or "Increment"
                        or "Decrement"
                        or "Add"
                        or "And"
                        or "Or";
                }

                return false;
            }

            staticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(caller.GetIdentifier(), out StaticFieldUsageTrack? staticFieldReferenceData);

            Dictionary<Instruction, List<Instruction>> jumpSites = this.GetMethodJumpSites(caller);

            if (callGraph.MediatedCallGraph.TryGetValue(caller.GetIdentifier(), out MethodCallData? calls)) {
                foreach (MethodReferenceData useds in calls.UsedMethods) {
                    foreach (MethodDefinition callee in useds.ImplementedMethods()) {
                        myCalingMethods.TryAdd(callee.GetIdentifier(), callee);
                    }
                }
            }

            foreach (Instruction? instruction in caller.Body.Instructions) {

                switch (instruction.OpCode.Code) {
                    case Code.Stfld: {
                            FieldDefinition? field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                break;
                            }

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, instruction, jumpSites)) {
                                MonoModCommon.Stack.StackTopTypePath loadModifyingInstance = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[0].Instructions.Last(), jumpSites)
                                    .First();

                                if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(caller, loadModifyingInstance.RealPushValueInstruction), out AggregatedStaticFieldProvenance? stackValueTrace)) {
                                    continue;
                                }

                                foreach (StaticFieldProvenance willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                    foreach (StaticFieldTracingChain part in willBeModified.PartTracingPaths) {
                                        if (part.EncapsulationHierarchy.Length == 0) {
                                            AddField(storedFields, willBeModified.TracingStaticField);
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
                            var methodRef = (MethodReference)instruction.Operand;
                            MethodDefinition? resolvedCallee = methodRef.TryResolve();

                            // Get all implementations of the called method
                            MethodDefinition[] implementations = this.GetMethodImplementations(caller, instruction, jumpSites, out _);

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            // Analyze the parameters of the called method
                            MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(caller, instruction, jumpSites);
                            MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                            for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path = paramPaths[i];
                                loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                                for (int j = 0; j < path.ParametersSources.Length; j++) {
                                    loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[j].Instructions.Last(), jumpSites).First();
                                }
                            }

                            if (
                                // Multidimensional array does not exist a method definition that can be resolved
                                (resolvedCallee is null && methodRef.DeclaringType is ArrayType arrayType && methodRef.Name is "Address" or "Set")

                                // The called method is a collection method
                                || (resolvedCallee is not null && CollectionElementLayer.IsModificationMethod(typeInheritanceGraph, caller, instruction))

                                // The called method performs atomic modification to the ref target
                                || IsAtomicModificationMethod(methodRef)

                                // The called method return a reference
                                || (methodRef.ReturnType is ByReferenceType && methodRef.Name is "get_Item")) {

                                foreach (MonoModCommon.Stack.StackTopTypePath[] paramGroup in loadParamsInEveryPaths) {
                                    MonoModCommon.Stack.StackTopTypePath loadInstance = paramGroup[0];

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(caller, loadInstance.RealPushValueInstruction), out AggregatedStaticFieldProvenance? stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (StaticFieldProvenance willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        AddField(storedFields, willBeModified.TracingStaticField);
                                    }
                                }

                                continue;
                            }

                            if (resolvedCallee is null) {
                                continue;
                            }

                            foreach (MethodDefinition implCallee in implementations) {

                                foreach (MonoModCommon.Stack.StackTopTypePath[] paramGroup in loadParamsInEveryPaths) {
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

                                        MonoModCommon.Stack.StackTopTypePath loadParam = paramGroup[paramIndex];

                                        // If the callMethod do not modify any Parameter, skip
                                        if (!paramModificationAnalyzer.ModifiedParameters.TryGetValue(implCallee.GetIdentifier(), out ImmutableDictionary<int, ParameterMutationInfo>? modifiedParameters)) {
                                            continue;
                                        }
                                        // If the input argument is not modified by the callMethod, skip
                                        if (!modifiedParameters.TryGetValue(paramIndex, out ParameterMutationInfo? modifiedParameter)) {
                                            continue;
                                        }

                                        // If the input argument is not coming from a static field, skip
                                        if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                            StaticFieldUsageTrack.GenerateStackKey(caller, loadParam.RealPushValueInstruction),
                                            out AggregatedStaticFieldProvenance? stackValueTrace)) {
                                            continue;
                                        }

                                        foreach (StaticFieldProvenance referencedStaticField in stackValueTrace.TracedStaticFields.Values) {
                                            List<MemberAccessStep[]> chains = [];
                                            foreach (StaticFieldTracingChain part in referencedStaticField.PartTracingPaths) {
                                                foreach (ParameterMutationInfo willBeModified in modifiedParameters.Values) {
                                                    foreach (ModifiedComponent modification in willBeModified.Mutations) {
                                                        if (part.EncapsulationHierarchy.Length > 0) {
                                                            if (modification.ModificationAccessPath.Length <= part.EncapsulationHierarchy.Length) {
                                                                continue;
                                                            }
                                                            bool isLeadingChain = true;
                                                            for (int i = 0; i < part.EncapsulationHierarchy.Length; i++) {
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
                                                AddField(storedFields, referencedStaticField.TracingStaticField);
                                                chains.Clear();
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    case Code.Ldsflda: {
                            if (MonoModCommon.Stack.TraceStackValueConsumers(caller, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
                                break;
                            }
                            goto case Code.Stsfld;
                        }
                    case Code.Stsfld: {
                            FieldDefinition? field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                break;
                            }
                            AddField(storedFields, field);
                            break;
                        }
                    case Code.Ldelema: {
                            if (MonoModCommon.Stack.TraceStackValueConsumers(caller, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
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

                            foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> callPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, instruction, jumpSites)) {
                                foreach (MonoModCommon.Stack.StackTopTypePath loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, callPath.ParametersSources[0].Instructions.Last(), jumpSites)) {

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                        StaticFieldUsageTrack.GenerateStackKey(caller, loadInstance.RealPushValueInstruction),
                                        out AggregatedStaticFieldProvenance? stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (StaticFieldProvenance willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        foreach (StaticFieldTracingChain part in willBeModified.PartTracingPaths) {
                                            if (part.EncapsulationHierarchy.Length == 0) {
                                                AddField(storedFields, willBeModified.TracingStaticField);
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                }
            }

            callees = [.. myCalingMethods.Values];
        }
    }
}
