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

namespace OTAPI.UnifiedServerProcess.Core.Analysis {
    public class ParamModificationAnalyzer : Analyzer, IMethodBehaivorFeature {
        public sealed override string Name => "ParamModificationAnalyzer";
        public readonly ImmutableDictionary<string, ImmutableDictionary<int, ParameterDefinition>> ModifiedParameters;
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
            this.delegateInvocationGraph = invocationGraph;
            this.methodInheritanceGraph = methodInheritanceGraph;

            var modifiedParameters = new Dictionary<string, Dictionary<int, ParameterDefinition>>();

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
                        foreach (var parameter in modifiedParameters[method.GetIdentifier()].Values) {
                            Progress(iteration, progress, currentWorkBatch.Length, "modified parameter: {0}", indent: 2, parameter.GetDebugName());
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
        static ImmutableDictionary<string, ImmutableDictionary<int, ParameterDefinition>> BuildResultDictionary(Dictionary<string, Dictionary<int, ParameterDefinition>> contents) {
            var builder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<int, ParameterDefinition>>();
            foreach (var (key, value) in contents) {
                builder.Add(key, value.ToImmutableDictionary(x => x.Key, x => x.Value));
            }
            return builder.ToImmutable();
        }

        void ProcessMethod(
                ModuleDefinition module,
                MethodDefinition method,
                ParameterFlowAnalyzer parameterFlowAnalyzer,
                Dictionary<string, Dictionary<int, ParameterDefinition>> modifiedParameters,
                out bool dataChanged) {

            var hasExternalChange = false;

            if (!method.HasBody) {
                dataChanged = false;
                return;
            }

            var jumpSites = this.GetMethodJumpSites(method);
            var processingMethodId = method.GetIdentifier();
            modifiedParameters.TryGetValue(processingMethodId, out var modifiedParametersCurrentMethod);

            foreach (var instruction in method.Body.Instructions) {
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


            void HandleModifyField(Instruction instruction) {
                var field = (FieldReference)instruction.Operand;
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                        if (!parameterFlowAnalyzer.AnalyzedMethods.TryGetValue(processingMethodId, out var tracedMethodData)) {
                            continue;
                        }
                        if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), out var tracedStackData)) {
                            continue;
                        }

                        foreach (var referencedParameter in tracedStackData.ParameterOrigins.Values) {

                            // Ignore the modification of "this" in constructors, we only care about input parameters.
                            if (referencedParameter.SourceParameter.IsParameterThis(method) && method.IsConstructor) {
                                continue;
                            }

                            if (modifiedParametersCurrentMethod is null) {
                                modifiedParameters.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                            }

                            if (modifiedParametersCurrentMethod.TryAdd(referencedParameter.SourceParameter.IndexWithThis(method), referencedParameter.SourceParameter)) {
                                hasExternalChange = true;
                            }
                        }
                    }
                }
            }
            void HandleModifyArrayElement(Instruction instruction) {
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                    foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSites)) {

                        if (!parameterFlowAnalyzer.AnalyzedMethods.TryGetValue(processingMethodId, out var tracedMethodData)) {
                            continue;
                        }
                        if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), out var tracedStackData)) {
                            continue;
                        }

                        foreach (var referencedParameter in tracedStackData.ParameterOrigins.Values) {

                            // Ignore the modification of "this" in constructors, we only care about input parameters.
                            if (referencedParameter.SourceParameter.IsParameterThis(method) && method.IsConstructor) {
                                continue;
                            }

                            if (modifiedParametersCurrentMethod is null) {
                                modifiedParameters.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                            }

                            if (modifiedParametersCurrentMethod.TryAdd(referencedParameter.SourceParameter.IndexWithThis(method), referencedParameter.SourceParameter)) {
                                hasExternalChange = true;
                            }
                        }
                    }
                }
            }
            void HandleInstanceModify(ParameterReferenceData tracedMethodData, MonoModCommon.Stack.StackTopTypePath[][] paths) {
                foreach (var paramGroup in paths) {
                    var loadInstance = paramGroup[0];

                    // If loadValue.RealPushValueInstruction is not coming from the parameter of caller method, skip
                    if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadInstance.RealPushValueInstruction), out var tracedStackData)) {
                        continue;
                    }

                    foreach (var referencedParameter in tracedStackData.ParameterOrigins.Values) {

                        // Ignore the modification of "this" in constructors, we only care about input parameters.
                        if (referencedParameter.SourceParameter.IsParameterThis(method) && method.IsConstructor) {
                            continue;
                        }

                        if (modifiedParametersCurrentMethod is null) {
                            modifiedParameters.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                        }

                        if (modifiedParametersCurrentMethod.TryAdd(referencedParameter.SourceParameter.IndexWithThis(method), referencedParameter.SourceParameter)) {
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

                // Analyze parameter sources
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
                        HandleInstanceModify(tracedMethodData, loadParamsInEveryPaths);
                    }

                    // Defination in unloaded assembly, just skip
                    return;
                }
                if (CollectionElementLayer.IsModificationMethod(typeInheritanceGraph, method, instruction)) {
                    HandleInstanceModify(tracedMethodData, loadParamsInEveryPaths);
                    return;
                }

                // We assume that if "this" is modified in invocation,
                // it's equivalent to parameter "this" has modified when the delegate is created

                if (resolvedCallee.DeclaringType.IsDelegate() && resolvedCallee.IsConstructor) {
                    var delegateLoadKey = DelegateInvocationData.GenerateStackKey(method, instruction);

                    if (!delegateInvocationGraph.TracedDelegates.TryGetValue(delegateLoadKey, out var traceData)) {
                        Warn(1, "No delegate trace data on {0}", instruction);
                        return;
                    }

                    if (!traceData.Invocations.Values.Any(invocation =>
                        modifiedParameters.TryGetValue(invocation.GetIdentifier(), out var modifiedParams)
                        && modifiedParams.ContainsKey(0))) {
                        return;
                    }

                    var sources = loadParamsInEveryPaths.Single();
                    var loadThisObject = sources[0].RealPushValueInstruction;

                    if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadThisObject), out var tracedStackData)) {
                        return;
                    }

                    foreach (var referencedParameter in tracedStackData.ParameterOrigins.Values) {

                        // Ignore the modification of "this" in constructors, we only care about input parameters.
                        if (referencedParameter.SourceParameter.IsParameterThis(method) && method.IsConstructor) {
                            continue;
                        }

                        if (modifiedParametersCurrentMethod is null) {
                            modifiedParameters.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                        }

                        if (modifiedParametersCurrentMethod.TryAdd(referencedParameter.SourceParameter.IndexWithThis(method), referencedParameter.SourceParameter)) {
                            hasExternalChange = true;
                        }
                    }

                    return;
                }

                foreach (var implCallee in implementations) {

                    // Make sure the callee implementation will modify inputing parameters
                    if (!modifiedParameters.TryGetValue(implCallee.GetIdentifier(), out var parametersWillBeModified)) {
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

                            if (!parametersWillBeModified.ContainsKey(paramIndex)) {
                                continue;
                            }

                            var loadValue = paramGroup[paramIndex];
                            // If loadValue.RealPushValueInstruction is not coming from the parameter of caller method, skip
                            if (!tracedMethodData.StackValueTraces.TryGetTrace(ParameterReferenceData.GenerateStackKey(method, loadValue.RealPushValueInstruction), out var tracedStackData)) {
                                continue;
                            }

                            foreach (var referencedParameter in tracedStackData.ParameterOrigins.Values) {

                                // Ignore the modification of "this" in constructors, we only care about input parameters.
                                if (referencedParameter.SourceParameter.IsParameterThis(method) && method.IsConstructor) {
                                    continue;
                                }

                                if (modifiedParametersCurrentMethod is null) {
                                    modifiedParameters.Add(processingMethodId, modifiedParametersCurrentMethod = []);
                                }

                                if (modifiedParametersCurrentMethod.TryAdd(referencedParameter.SourceParameter.IndexWithThis(method), referencedParameter.SourceParameter)) {
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
