using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis
{
    public class MethodCallGraph : Analyzer, IMethodBehaivorFeature
    {
        public override string Name => "MethodCallGraph";

        public readonly ImmutableDictionary<string, MethodCallData> MediatedCallGraph;

        readonly DelegateInvocationGraph delegateInvocationGraph;
        readonly MethodInheritanceGraph methodInheritanceGraph;
        public DelegateInvocationGraph DelegateInvocationGraph => delegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => methodInheritanceGraph;

        public MethodCallGraph(ILogger logger, ModuleDefinition module, DelegateInvocationGraph invocationGraph, MethodInheritanceGraph inheritanceGraph) : base(logger) {
            delegateInvocationGraph = invocationGraph;
            methodInheritanceGraph = inheritanceGraph;


            var usedMethodsDict = new Dictionary<string, HashSet<MethodReferenceData>>();
            var usedByMethodsDict = new Dictionary<string, HashSet<MethodDefinition>>();
            var unexpectedImplMissMethods = new Dictionary<string, MethodDefinition>();

            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody)
                        continue;

                    var methodId = method.GetIdentifier();

                    var body = method.Body;

                    var jumpSites = this.GetMethodJumpSites(method);

                    foreach (var instruction in body.Instructions) {
                        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt && instruction.OpCode != OpCodes.Newobj)
                            continue;

                        var calleeRef = (MethodReference)instruction.Operand;

                        MethodDefinition? calleeDef = calleeRef.TryResolve();
                        if (calleeDef is null) {
                            continue;
                        }

                        int taskRunDeleIndex = -1;
                        if (calleeDef.DeclaringType.Name.OrdinalStartsWith(nameof(TaskFactory)) && calleeDef.Name.OrdinalStartsWith(nameof(TaskFactory.StartNew))) {
                            taskRunDeleIndex = 1;
                        }
                        else if (calleeDef.DeclaringType.Name.OrdinalStartsWith(nameof(Task)) && calleeDef.Name.OrdinalStartsWith(nameof(Task.Run))) {
                            taskRunDeleIndex = 0;
                        }
                        else if (calleeDef.DeclaringType.Name == nameof(ThreadPool) && calleeDef.Name == nameof(ThreadPool.QueueUserWorkItem)) {
                            taskRunDeleIndex = 0;
                        }
                        else if (calleeDef.IsConstructor && calleeDef.DeclaringType.Name is nameof(Thread) or nameof(Task)) {
                            taskRunDeleIndex = 0;
                        }
                        if (taskRunDeleIndex != -1) {
                            foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSites)) {
                                var loadDelegate = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[taskRunDeleIndex].Instructions.Last(), jumpSites)
                                    .Single();
                                if (!invocationGraph.TracedDelegates.TryGetValue(DelegateInvocationData.GenerateStackKey(method, loadDelegate.RealPushValueInstruction), out var data)) {
                                    continue;
                                }
                                AddMethod(MethodReferenceData.DelegateCall(calleeDef, [.. data.Invocations.Values]));
                            }
                        }

                        var implementations = this.GetMethodImplementations(method, instruction, jumpSites, out var isDelegateInvocation, true);

                        var calleeId = calleeDef.GetIdentifier();

                        if (implementations.Length == 0) {
                            if (!calleeDef.DeclaringType.IsInterface && !calleeDef.IsAbstract && !calleeDef.DeclaringType.IsDelegate()) {
                                unexpectedImplMissMethods.TryAdd(calleeId, calleeDef);
                            }
                        }

                        if (isDelegateInvocation) {
                            AddMethod(MethodReferenceData.DelegateCall(calleeDef, implementations));
                        }
                        else {
                            AddMethod(MethodReferenceData.InheritanceCall(calleeDef, [.. implementations.Where(x => x.GetIdentifier() != calleeId)]));
                        }

                        if (!usedByMethodsDict.TryGetValue(calleeId, out var usedByMethods)) {
                            usedByMethods = [];
                            usedByMethodsDict[calleeId] = usedByMethods;
                        }
                        usedByMethods.Add(method);

                        void AddMethod(MethodReferenceData add) {
                            // Update usedMethodsDict for the tail method
                            if (!usedMethodsDict.TryGetValue(methodId, out var usedMethods)) {
                                usedMethods = [];
                                usedMethodsDict[methodId] = usedMethods;
                            }
                            usedMethods.Add(add);

                            var implId = add.DirectlyCalledMethod.GetIdentifier();

                            // Update usedByMethodsDict for the implementation method
                            if (!usedByMethodsDict.TryGetValue(implId, out var usedByMethods)) {
                                usedByMethods = [];
                                usedByMethodsDict[implId] = usedByMethods;
                            }
                            usedByMethods.Add(method);
                        }
                    }
                }
            }

            foreach (var unexpectedImplMissMethod in unexpectedImplMissMethods.Values) {
                Error("Method missing implementation: {0}", unexpectedImplMissMethod.GetDebugName(true));
            }

            Dictionary<string, MethodDefinition> allMethods = [];

            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    allMethods.TryAdd(method.GetIdentifier(), method);
                }
            }
            foreach (var useds in usedMethodsDict.Values) {
                foreach (var used in useds) {
                    allMethods.TryAdd(used.DirectlyCalledMethod.GetIdentifier(), used.DirectlyCalledMethod);
                    foreach (var implicitCall in used.ImplicitlyCalledMethods) {
                        allMethods.TryAdd(implicitCall.GetIdentifier(), implicitCall);
                    }
                }
            }

            var methodCallsBuilder = ImmutableDictionary.CreateBuilder<string, MethodCallData>();
            foreach (var method in allMethods.Values) {

                var methodId = method.GetIdentifier();

                MethodReferenceData[] usedMethods = usedMethodsDict.TryGetValue(methodId, out var um)
                    ? [.. um]
                    : [];
                MethodDefinition[] usedByMethods = usedByMethodsDict.TryGetValue(methodId, out var ubm)
                    ? [.. ubm]
                    : [];

                methodCallsBuilder.Add(method.GetIdentifier(), new MethodCallData(method, usedMethods, usedByMethods));
            }

            MediatedCallGraph = methodCallsBuilder.ToImmutable();
        }
    }
}
