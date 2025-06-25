using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IMethodImplementationFeature : ILoggedComponent
    {
        DelegateInvocationGraph DelegateInvocationGraph { get; }
        MethodInheritanceGraph MethodInheritanceGraph { get; }
    }
    public static class MethodBehaivorFeatureExtensions
    {
        public static MethodDefinition[] GetMethodImplementations<TFeature>(this TFeature point,
            MethodDefinition caller,
            Instruction callInstruction,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            out bool isDelegateInvocation,
            bool noWarings = false)
            where TFeature : LoggedComponent, IMethodImplementationFeature {

            if (callInstruction.OpCode != OpCodes.Callvirt && callInstruction.OpCode != OpCodes.Call && callInstruction.OpCode != OpCodes.Newobj) {
                throw new Exception("Expected callvirt or call");
            }
            isDelegateInvocation = false;

            var callee = ((MethodReference)callInstruction.Operand).TryResolve();

            if (callee is null) {
                if (!noWarings) point.Warn(1, "Could not resolve {0}", callInstruction.Operand);
                return [];
            }

            if (callee.DeclaringType.IsDelegate() && (callee.Name == nameof(Action.Invoke) || callee.Name == nameof(Action.BeginInvoke))) {
                isDelegateInvocation = true;
                Dictionary<string, Instruction> keys = [];
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, callInstruction, jumpSites)) {
                    foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                        if (loadValue.StackTopType?.IsDelegate() ?? false) {
                            var delegateLoadKey = DelegateInvocationData.GenerateStackKey(caller, loadValue.RealPushValueInstruction);
                            keys.TryAdd(delegateLoadKey, loadValue.RealPushValueInstruction);
                        }
                    }
                }
                Dictionary<string, MethodDefinition> result = [];
                foreach (var keyValuePair in keys) {
                    if (point.DelegateInvocationGraph.TracedDelegates.TryGetValue(keyValuePair.Key, out var traceData)) {
                        foreach (var invocation in traceData.Invocations) {
                            result.Add(invocation.Key, invocation.Value);
                        }
                    }
                    else if (!noWarings) {
                        point.Warn(1, "While processing method {0}", caller);
                        point.Warn(1, "at delegate invoke instruction {0}", callInstruction);
                        point.Warn(1, "found no invocation in delegate instance {0}", keyValuePair.Value);
                    }
                }
                return [.. result.Values];
            }
            else {
                MethodDefinition realCallee;
                if (!callee.IsStatic && callee.IsVirtual && callInstruction.OpCode == OpCodes.Callvirt) {
                    HashSet<Instruction> loadInstances = [];
                    foreach (var callPath in MonoModCommon.Stack.AnalyzeParametersSources(caller, callInstruction, jumpSites)) {
                        loadInstances.Add(callPath.ParametersSources[0].Instructions.Last());
                    }
                    if (loadInstances.Count == 1) {
                        var type = MonoModCommon.Stack.AnalyzeStackTopType(caller, loadInstances.First(), jumpSites);
                        realCallee = type?.TryResolve()?.Methods.FirstOrDefault(m => m.GetIdentifier(false) == callee.GetIdentifier(false)) ?? callee;
                    }
                    else {
                        realCallee = callee;
                    }
                }
                else {
                    realCallee = callee;
                }
                if (point.MethodInheritanceGraph.CheckedMethodImplementationChains.TryGetValue(realCallee.GetIdentifier(), out var implementations)) {
                    return implementations;
                }

                if (!callee.HasBody) {
                    if (!noWarings) point.Warn(1, "No method implementations {0}", callee.GetDebugName());
                    return [];
                }

                // It's mean that method from core reference library, that has no body
                if (callee.ReturnType.FullName == callee.Module.TypeSystem.Void.FullName
                    && callee.Module.Name.OrdinalStartsWith("System")
                    && callee.Body.Instructions.Count == 1
                    && callee.Body.Instructions[0].OpCode == OpCodes.Ret) {
                    return [];
                }
                if (callee.Body.Instructions.Count == 2
                    && callee.Module.Name.OrdinalStartsWith("System")
                    && callee.Body.Instructions[0].OpCode == OpCodes.Ldnull
                    && callee.Body.Instructions[1].OpCode == OpCodes.Throw) {
                    return [];
                }

                return [callee];
            }
        }
    }
}
