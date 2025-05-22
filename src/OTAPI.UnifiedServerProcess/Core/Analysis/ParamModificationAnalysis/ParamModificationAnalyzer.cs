using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis {
    public class ParamModificationAnalyzer : Analyzer, IMethodBehaivorFeature {
        public sealed override string Name => "ParamModificationAnalyzer";
        public readonly ImmutableDictionary<string, ImmutableDictionary<int, ParamModifications>> ModifiedParameters;
        readonly TypeInheritanceGraph typeInheritanceGraph;

        readonly DelegateInvocationGraph delegateInvocationGraph;
        readonly MethodInheritanceGraph methodInheritanceGraph;
        public DelegateInvocationGraph DelegateInvocationGraph => delegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => methodInheritanceGraph;
        public ParamModificationAnalyzer(ILogger logger,
            ModuleDefinition module,
            ParameterFlowAnalyzer parameterFlowAnalyzer,
            MethodCallGraph callGraph,
            DelegateInvocationGraph invocationGraph,
            MethodInheritanceGraph methodInheritanceGraph,
            TypeInheritanceGraph typeInheritanceGraph)
            : base(logger) {

            this.typeInheritanceGraph = typeInheritanceGraph;
            delegateInvocationGraph = invocationGraph;
            this.methodInheritanceGraph = methodInheritanceGraph;

            var modifiedParameters = new Dictionary<string, Dictionary<int, ParamModifications>>();

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
                        parameterFlowAnalyzer,
                        modifiedParameters,
                        out bool dataChanged
                    );
                    workQueue.Remove(method.GetIdentifier());

                    if (dataChanged) {
                        foreach (var modification in modifiedParameters[method.GetIdentifier()].Values) {
                            Progress(iteration, progress, currentWorkBatch.Length, "modified modifications: {0}", indent: 2, modification.parameter.GetDebugName());
                        }
                    }
                    if (dataChanged && callGraph.MediatedCallGraph.TryGetValue(method.GetIdentifier(), out var callers)) {
                        foreach (var caller in callers.UsedByMethods) {
                            if (workQueue.TryAdd(caller.GetIdentifier(), caller)) {
                                Progress(iteration, progress, currentWorkBatch.Length, "Add: {0}", indent: 1, caller.GetDebugName());
                            }
                        }
                    }
                }
            }

            ModifiedParameters = BuildResultDictionary(modifiedParameters);
        }
        static ImmutableDictionary<string, ImmutableDictionary<int, ParamModifications>> BuildResultDictionary(Dictionary<string, Dictionary<int, ParamModifications>> contents) {
            var builder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<int, ParamModifications>>();
            foreach (var (key, value) in contents) {
                builder.Add(key, value.ToImmutableDictionary(x => x.Key, x => x.Value));
            }
            return builder.ToImmutable();
        }

        void ProcessMethod(
            ModuleDefinition module,
            MethodDefinition method,
            ParameterFlowAnalyzer parameterFlowAnalyzer,
            Dictionary<string, Dictionary<int, ParamModifications>> modifiedParametersAllMethods,
            out bool dataChanged) {

            var hasExternalChange = false;

            if (!method.HasBody) {
                dataChanged = false;
                return;
            }

            var jumpSites = this.GetMethodJumpSites(method);
            var processingMethodId = method.GetIdentifier();

            bool isCatchBlock = false;

            static bool CheckIsCatchBlock(MethodBody body, Instruction processingInstruction, ref bool isCatchBlock) {
                foreach (var exhandler in body.ExceptionHandlers) {
                    if (exhandler.HandlerStart == processingInstruction) {
                        isCatchBlock = true;
                        return true;
                    }
                    if (exhandler.HandlerEnd == processingInstruction) {
                        isCatchBlock = false;
                        return true;
                    }
                }
                return false;
            }

            foreach (var instruction in method.Body.Instructions) {

                if (CheckIsCatchBlock(method.Body, instruction, ref isCatchBlock)) {
                    continue;
                }
                // the modifications in a catch block is a unexpected behavior
                // we can ignore it (such as Terraria.Localization.NetworkText.ToString() will SetToEmptyLiteral() when cause an exception)
                if (isCatchBlock) {
                    continue;
                }
                switch (instruction.OpCode.Code) {

                    case Code.Stfld:
                    case Code.Ldflda:
                        HandleModifyField(instruction);
                        break;

                    case Code.Stelem_Any:
                    case Code.Stelem_I:
                    case Code.Stelem_I1:
                    case Code.Stelem_I2:
                    case Code.Stelem_I4:
                    case Code.Stelem_I8:
                    case Code.Stelem_R4:
                    case Code.Stelem_R8:
                    case Code.Stelem_Ref:
                    case Code.Ldelema:
                        HandleModifyArrayElement(instruction);
                        break;

                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(instruction);
                        break;
                }
            }

            dataChanged = hasExternalChange;

            static bool CheckAndAddModifications(Dictionary<string, Dictionary<int, ParamModifications>> modifiedParametersAllMethods, string processingMethodId, MethodDefinition method, ParameterTrackingManifest modified) {
                
                if (!modifiedParametersAllMethods.TryGetValue(processingMethodId, out var modifiedParametersCurrentMethod)) {
                    modifiedParametersCurrentMethod = [];
                }

                var id = modified.TrackedParameter.IndexWithThis(method);

                if (!modifiedParametersCurrentMethod.TryGetValue(id, out var modifications)) {
                    modifications = new ParamModifications(modified.TrackedParameter);
                }

                var result = false;
                foreach (var part in modified.PartTrackingPaths) {
                    if (part.ComponentAccessPath.Length == 0) {
                        continue;
                    }
                    if (!modifications.modifications.Add(new ModifiedComponent(part.ComponentAccessPath))) {
                        continue;
                    }
                    result = true;
                }

                if (result) {
                    modifiedParametersCurrentMethod.TryAdd(id, modifications);
                    modifiedParametersAllMethods.TryAdd(processingMethodId, modifiedParametersCurrentMethod);
                }

                return result;
            }

            static bool TryAddModifications(Dictionary<string, Dictionary<int, ParamModifications>> modifiedParametersAllMethods, string processingMethodId, MethodDefinition method, ParameterDefinition parameter, IEnumerable<MemberAccessStep[]> modifications) {
                
                if (!modifiedParametersAllMethods.TryGetValue(processingMethodId, out var modifiedParametersCurrentMethod)) {
                    modifiedParametersCurrentMethod = [];
                }

                var id = parameter.IndexWithThis(method);

                if (!modifiedParametersCurrentMethod.TryGetValue(id, out var modification)) {
                    modification = new ParamModifications(parameter);
                }

                bool result = false;
                foreach (var modified in modifications) {
                    if (!modification.modifications.Add(new ModifiedComponent(modified))) {
                        continue;
                    }
                    result = true;
                }

                if (result) {
                    modifiedParametersCurrentMethod.TryAdd(id, modification);
                    modifiedParametersAllMethods.TryAdd(processingMethodId, modifiedParametersCurrentMethod);
                }

                return result;
            }

            void HandleModifyField(Instruction instruction) {
                var field = (FieldReference)instruction.Operand;
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadModifyingInstance in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                        if (!parameterFlowAnalyzer.AnalyzedMethods.TryGetValue(processingMethodId, out var tracedMethodData)) {
                            continue;
                        }
                        if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadModifyingInstance.RealPushValueInstruction), out var tracedStackData)) {
                            continue;
                        }

                        if (tracedStackData.ReferencedParameters.Values
                            .SelectMany(p => p.PartTrackingPaths)
                            .Any(o => o.EncapsulationHierarchy.Length >= 1 && o.EncapsulationHierarchy.Last() is RealMemberLayer memberLayer && memberLayer.Member.FullName == field.FullName)) {
                            continue;
                        }

                        if (!tracedStackData.TryTrackMemberLoad(field, out var modified)) {
                            continue;
                        }

                        foreach (var modifiedParameter in modified.ReferencedParameters.Values) {
                            // Ignore the modificationAccessChain of "this" in constructors, we only care about input parameters.
                            if (modifiedParameter.TrackedParameter.IsParameterThis(method) && method.IsConstructor) {
                                continue;
                            }
                            if (CheckAndAddModifications(modifiedParametersAllMethods, processingMethodId, method, modifiedParameter)) {
                                hasExternalChange = true;
                            }
                        }
                    }
                }
            }
            void HandleModifyArrayElement(Instruction instruction) {
                if (!parameterFlowAnalyzer.AnalyzedMethods.TryGetValue(processingMethodId, out var tracedMethodData)) {
                    return;
                }

                var paramPaths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites);
                MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                    var path = paramPaths[i];
                    loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                    for (int j = 0; j < path.ParametersSources.Length; j++) {
                        loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSites).First();
                    }
                }

                HandleModifyArrayElementInner(tracedMethodData, loadParamsInEveryPaths);
            }
            void HandleModifyArrayElementInner(ParameterReferenceData tracedMethodData, MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths) {
                foreach (var paramGroup in loadParamsInEveryPaths) {
                    var loadModifyingInstance = paramGroup[0];

                    // If loadModifyingInstance.RealPushValueInstruction is not coming from the modifications of caller method, skip
                    if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadModifyingInstance.RealPushValueInstruction), out var tracedStackData)) {
                        continue;
                    }
                    if (!tracedStackData.TryTrackArrayElementLoad((ArrayType)loadModifyingInstance.StackTopType!, out var modified)) {
                        continue;
                    }
                    foreach (var modifiedParameter in tracedStackData.ReferencedParameters.Values) {
                        // Ignore the modificationAccessChain of "this" in constructors, we only care about input parameters.
                        if (modifiedParameter.TrackedParameter.IsParameterThis(method) && method.IsConstructor) {
                            continue;
                        }
                        if (CheckAndAddModifications(modifiedParametersAllMethods, processingMethodId, method, modifiedParameter)) {
                            hasExternalChange = true;
                        }
                    }
                }
            }
            void HandleModifyCollectionElement(ParameterReferenceData tracedMethodData, MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths, Instruction modifyingInstruction) {
                foreach (var paramGroup in loadParamsInEveryPaths) {
                    var loadModifyingInstance = paramGroup[0];

                    // If loadModifyingInstance.RealPushValueInstruction is not coming from the modifications of caller method, skip
                    if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadModifyingInstance.RealPushValueInstruction), out var tracedStackData)) {
                        continue;
                    }
                    var collectionType = ((MethodReference)modifyingInstruction.Operand).DeclaringType;
                    var elementType = collectionType is GenericInstanceType generic ? generic.GenericArguments.Last() : collectionType.Module.TypeSystem.Object;
                    if (!tracedStackData.TryTrackCollectionElementLoad(collectionType, elementType, out var modified)) {
                        continue;
                    }
                    foreach (var modifiedParameter in tracedStackData.ReferencedParameters.Values) {
                        // Ignore the modificationAccessChain of "this" in constructors, we only care about input parameters.
                        if (modifiedParameter.TrackedParameter.IsParameterThis(method) && method.IsConstructor) {
                            continue;
                        }
                        if (CheckAndAddModifications(modifiedParametersAllMethods, processingMethodId, method, modifiedParameter)) {
                            hasExternalChange = true;
                        }
                    }
                }
            }
            void HandleMethodCall(Instruction instruction) {

                var callee = (MethodReference)instruction.Operand;
                var resolvedCallee = ((MethodReference)instruction.Operand).TryResolve();

                if (!parameterFlowAnalyzer.AnalyzedMethods.TryGetValue(processingMethodId, out var tracedMethodData)) {
                    return;
                }

                var implementations = this.GetMethodImplementations(method, instruction, jumpSites, out _);

                // Analyze modifications sources
                var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites);
                MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paramPaths.Length][];

                for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                    var path = paramPaths[i];
                    loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                    for (int j = 0; j < path.ParametersSources.Length; j++) {
                        loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSites).First();
                    }
                }

                if (resolvedCallee is null) {
                    // Multidimensional array does not exist a method definition that can be resolved
                    if (callee.DeclaringType is ArrayType && callee.Name is "Address" or "Set") {
                        HandleModifyArrayElementInner(tracedMethodData, loadParamsInEveryPaths);
                    }

                    // Defination in unloaded assembly, just skip
                    return;
                }
                if (CollectionElementLayer.IsModificationMethod(typeInheritanceGraph, method, instruction)) {
                    HandleModifyCollectionElement(tracedMethodData, loadParamsInEveryPaths, instruction);
                    return;
                }

                //// We assume that if "this" is modified in invocation,
                //// it's equivalent to modifications "this" has modified when the delegate is created

                //if (resolvedCallee.DeclaringType.IsDelegate() && resolvedCallee.IsConstructor) {
                //    var delegateLoadKey = DelegateInvocationData.GenerateStackKey(method, instruction);

                //    if (!delegateInvocationGraph.TracedDelegates.TryGetValue(delegateLoadKey, out var traceData)) {
                //        Warn(1, "No delegate trace data on {0}", instruction);
                //        return;
                //    }

                //    if (!traceData.Invocations.Values.Any(invocation =>
                //        modifiedParametersAllMethods.TryGetValue(invocation.GetIdentifier(), out var modifiedParams)
                //        && modifiedParams.ContainsKey(0))) {
                //        return;
                //    }

                //    var sources = loadParamsInEveryPaths.Single();
                //    var loadThisObject = sources[0].RealPushValueInstruction;

                //    if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadThisObject), out var tracedStackData)) {
                //        return;
                //    }

                //    foreach (var referencedParameter in tracedStackData.ReferencedParameters.Values) {
                //        // Ignore the modificationAccessChain of "this" in constructors, we only care about input parameters.
                //        if (referencedParameter.TrackedParameter.IsParameterThis(method) && method.IsConstructor) {
                //            continue;
                //        }

                //        if (modifiedParametersCurrentMethod is null) {
                //            modifiedParametersAllMethods.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                //        }

                //        if (modifiedParametersCurrentMethod.TryAddModifications(referencedParameter.TrackedParameter.IndexWithThis(method), referencedParameter.TrackedParameter)) {
                //            hasExternalChange = true;
                //        }
                //    }

                //    return;
                //}

                foreach (var implCallee in implementations) {

                    // Make sure the callee implementation will modify inputing parameters
                    if (!modifiedParametersAllMethods.TryGetValue(implCallee.GetIdentifier(), out var parametersWillBeModified)) {
                        continue;
                    }

                    foreach (var paramGroup in loadParamsInEveryPaths) {
                        for (int paramIndex = 0; paramIndex < paramGroup.Length; paramIndex++) {

                            var paramIndexInImpl = paramIndex;

                            // There are also delegate invocations in implementations
                            // so we use original 'resolvedMethod' instead 'implMethod'
                            // because the implMethod may be a static method,
                            // but the original method is a delegate's invoke method which is never static
                            if (!resolvedCallee.IsStatic && !resolvedCallee.IsConstructor) {
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

                            if (!parametersWillBeModified.TryGetValue(paramIndex, out var willBeModified)) {
                                continue;
                            }

                            var loadValue = paramGroup[paramIndex];
                            // If loadModifyingInstance.RealPushValueInstruction is not coming from the modifications of caller method, skip
                            if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), out var tracedStackData)) {
                                continue;
                            }

                            foreach (var referencedParameter in tracedStackData.ReferencedParameters.Values) {

                                // Ignore the modificationAccessChain of "this" in constructors, we only care about input parameters.
                                if (referencedParameter.TrackedParameter.IsParameterThis(method) && method.IsConstructor) {
                                    continue;
                                }
                                List<MemberAccessStep[]> chains = [];
                                foreach (var modification in willBeModified.modifications) {
                                    foreach (var part in referencedParameter.PartTrackingPaths) {
                                        if (part.ComponentAccessPath.Length > 0) {
                                            if (modification.modificationAccessChain.Length <= part.ComponentAccessPath.Length) {
                                                continue;
                                            }
                                            bool isLeadingChain = true;
                                            for (int i = 0; i < part.ComponentAccessPath.Length; i++) {
                                                if (modification.modificationAccessChain[i] != part.ComponentAccessPath[i]) {
                                                    isLeadingChain = false;
                                                    break;
                                                }
                                            }
                                            if (isLeadingChain) {
                                                chains.Add([.. modification.modificationAccessChain.Skip(part.ComponentAccessPath.Length)]);
                                            }
                                        }
                                        else {
                                            chains.Add([.. modification.modificationAccessChain.Skip(part.ComponentAccessPath.Length)]);
                                        }
                                    }
                                }

                                if (TryAddModifications(modifiedParametersAllMethods, processingMethodId, method, referencedParameter.TrackedParameter, chains)) {
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
