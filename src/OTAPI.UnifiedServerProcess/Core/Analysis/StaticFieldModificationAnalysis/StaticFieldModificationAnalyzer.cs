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

            foreach (var fieldKV in modifiedFields_ignoredInitOnlys) {
                modifiedFields_WhenInit.Remove(fieldKV.Key);
            }
            foreach (var kv in modifiedFields_WhenInit.ToArray()) {
                if (kv.Value.DeclaringType.Name.OrdinalStartsWith('<')) {
                    modifiedFields_WhenInit.Remove(kv.Key);
                }
            }

            initModifiedFields = [.. modifiedFields_WhenInit.Values];
            modifiedFields = [.. modifiedFields_ignoredInitOnlys.Values];

            return;
        }

        private Dictionary<string, FieldDefinition> FetchModifiedFieldInner(MethodDefinition[] entryPoints, HashSet<string> ignored) {
            var workQueue = entryPoints.ToDictionary(x => x.GetIdentifier(), x => x);

            var visited = new Dictionary<string, MethodDefinition>();

            var storedFields = new Dictionary<string, FieldDefinition>();

            int iteration = 0;
            while (workQueue.Count > 0) {
                iteration++;
                var currentWorkBatch = workQueue.Values.ToArray();

                for (int progress = 0; progress < currentWorkBatch.Length; progress++) {
                    var method = currentWorkBatch[progress];
                    Progress(iteration, progress, currentWorkBatch.Length, method.GetDebugName());
                    ProcessMethod(
                        method,
                        storedFields,
                        out var addedCallees
                    );

                    var methodId = method.GetIdentifier();
                    workQueue.Remove(methodId);
                    visited.TryAdd(methodId, method);

                    foreach (var callee in addedCallees) {
                        var calleeID = callee.GetIdentifier();
                        if (ignored.Contains(calleeID)) {
                            continue;
                        }
                        if (visited.ContainsKey(calleeID)) {
                            continue;
                        }
                        if (workQueue.TryAdd(calleeID, callee)) {
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
                if (field.Name == "scutlixEyePositions") {

                }
                dict.TryAdd(field.GetIdentifier(), field);
            }

            staticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(caller.GetIdentifier(), out var staticFieldReferenceData);

            var jumpSites = this.GetMethodJumpSites(caller);

            if (callGraph.MediatedCallGraph.TryGetValue(caller.GetIdentifier(), out var calls)) {
                foreach (var useds in calls.UsedMethods) {
                    foreach (var callee in useds.ImplementedMethods()) {
                        myCalingMethods.TryAdd(callee.GetIdentifier(), callee);
                    }
                }
            }

            foreach (var instruction in caller.Body.Instructions) {

                switch (instruction.OpCode.Code) {
                    case Code.Stfld: {
                            var field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                break;
                            }

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, instruction, jumpSites)) {
                                var loadModifyingInstance = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[0].Instructions.Last(), jumpSites)
                                    .First();

                                if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(caller, loadModifyingInstance.RealPushValueInstruction), out var stackValueTrace)) {
                                    continue;
                                }

                                foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                    foreach (var part in willBeModified.PartTracingPaths) {
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
                            var resolvedCallee = methodRef.TryResolve();

                            // Get all implementations of the called method
                            var implementations = this.GetMethodImplementations(caller, instruction, jumpSites, out _);

                            if (staticFieldReferenceData is null) {
                                continue;
                            }

                            // Analyze the parameters of the called method
                            var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(caller, instruction, jumpSites);
                            MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                            for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                                var path = paramPaths[i];
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

                                // The called method return a reference
                                || (methodRef.ReturnType is ByReferenceType && methodRef.Name is "get_Item")) {

                                foreach (var paramGroup in loadParamsInEveryPaths) {
                                    var loadInstance = paramGroup[0];

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(caller, loadInstance.RealPushValueInstruction), out var stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        AddField(storedFields, willBeModified.TracingStaticField);
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

                                        // If the input argument is not coming from a static field, skip
                                        if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                            StaticFieldUsageTrack.GenerateStackKey(caller, loadParam.RealPushValueInstruction),
                                            out var stackValueTrace)) {
                                            continue;
                                        }

                                        foreach (var referencedStaticField in stackValueTrace.TracedStaticFields.Values) {
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
                            if (MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
                                break;
                            }
                            goto case Code.Stsfld;
                        }
                    case Code.Stsfld: {
                            var field = ((FieldReference)instruction.Operand).TryResolve();
                            if (field is null) {
                                break;
                            }
                            AddField(storedFields, field);
                            break;
                        }
                    case Code.Ldelema: {
                            if (MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, instruction).All(inst => inst.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldfld or Code.Ldflda)) {
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

                            foreach (var callPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, instruction, jumpSites)) {
                                foreach (var loadInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, callPath.ParametersSources[0].Instructions.Last(), jumpSites)) {

                                    if (!staticFieldReferenceData.StackValueTraces.TryGetTrace(
                                        StaticFieldUsageTrack.GenerateStackKey(caller, loadInstance.RealPushValueInstruction),
                                        out var stackValueTrace)) {
                                        continue;
                                    }

                                    foreach (var willBeModified in stackValueTrace.TracedStaticFields.Values) {
                                        foreach (var part in willBeModified.PartTracingPaths) {
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
