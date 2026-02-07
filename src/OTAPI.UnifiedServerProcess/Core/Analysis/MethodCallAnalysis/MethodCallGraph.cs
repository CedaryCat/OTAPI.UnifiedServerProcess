using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis
{
    public class MethodCallGraph : Analyzer, IMethodImplementationFeature
    {
        public override string Name => "MethodCallGraph";
        
        public readonly Dictionary<string, MethodCallData> MediatedCallGraph;

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
                foreach (var caller in type.Methods) {
                    if (!caller.HasBody)
                        continue;

                    var methodId = caller.GetIdentifier();

                    var body = caller.Body;

                    var jumpSites = this.GetMethodJumpSites(caller);

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
                            foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, instruction, jumpSites)) {
                                var loadDelegate = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[taskRunDeleIndex].Instructions.Last(), jumpSites)
                                    .Single();
                                if (!invocationGraph.TracedDelegates.TryGetValue(DelegateInvocationData.GenerateStackKey(caller, loadDelegate.RealPushValueInstruction), out var data)) {
                                    continue;
                                }
                                AddMethod(MethodReferenceData.DelegateCall(calleeDef, [.. data.Invocations.Values]));
                            }
                        }

                        IEnumerable<MethodDefinition> methods = [caller];
                        if (calleeRef.HasThis && calleeRef.Name != ".ctor") {
                            HashSet<MethodDefinition> tempMethods = [];
                            foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, instruction, jumpSites)) {
                                foreach (var stackTop in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, path.ParametersSources[0].Instructions.Last(), jumpSites)) {
                                    var stackType = stackTop.StackTopType?.TryResolve();
                                    if (stackType is null) {
                                        continue;
                                    }
                                    if (stackType.Scope.Name != module.Name) {
                                        logger.Warn(this, 1, $"Ignore: [type:{stackType.Name}|callee: {calleeRef.GetDebugName()}] by {caller.GetDebugName()}");
                                        continue;
                                    }
                                    var m = stackType?.GetRuntimeMethods(true).FirstOrDefault(m => 
                                        m.GetIdentifier(false).EndsWith("." + calleeDef.GetIdentifier(false)) || // implict interface impl
                                        m.GetIdentifier(false) == calleeDef.GetIdentifier(false));
                                    if (m is not null) {
                                        tempMethods.Add(m);
                                    }
                                }
                            }
                            methods = tempMethods;
                        }

                        HashSet<MethodDefinition> implementations = [];
                        bool isDelegateInvocation = false;
                        foreach (var m in methods) {
                            foreach (var impl in this.GetMethodImplementations(caller, instruction, jumpSites, out isDelegateInvocation, true)) {
                                implementations.Add(impl);
                            }
                        }

                        var calleeId = calleeDef.GetIdentifier();

                        if (implementations.Count == 0) {
                            if (!calleeDef.DeclaringType.IsInterface && !calleeDef.IsAbstract && !calleeDef.DeclaringType.IsDelegate()) {
                                unexpectedImplMissMethods.TryAdd(calleeId, calleeDef);
                            }
                        }

                        if (isDelegateInvocation) {
                            AddMethod(MethodReferenceData.DelegateCall(calleeDef, [.. implementations]));
                        }
                        else {
                            AddMethod(MethodReferenceData.InheritanceCall(calleeDef, [.. implementations.Where(x => x.GetIdentifier() != calleeId)]));
                        }

                        if (!usedByMethodsDict.TryGetValue(calleeId, out var usedByMethods)) {
                            usedByMethods = [];
                            usedByMethodsDict[calleeId] = usedByMethods;
                        }
                        usedByMethods.Add(caller);

                        void AddMethod(MethodReferenceData add) {
                            // Update usedMethodsDict for the tail caller
                            if (!usedMethodsDict.TryGetValue(methodId, out var usedMethods)) {
                                usedMethods = [];
                                usedMethodsDict[methodId] = usedMethods;
                            }
                            usedMethods.Add(add);

                            var implId = add.DirectlyCalledMethod.GetIdentifier();

                            // Update usedByMethodsDict for the implementation caller
                            if (!usedByMethodsDict.TryGetValue(implId, out var usedByMethods)) {
                                usedByMethods = [];
                                usedByMethodsDict[implId] = usedByMethods;
                            }
                            usedByMethods.Add(caller);
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

            var methodCallsBuilder = new Dictionary<string, MethodCallData>();
            foreach (var method in allMethods.Values) {

                var methodId = method.GetIdentifier();

                MethodReferenceData[] usedMethods = usedMethodsDict.TryGetValue(methodId, out var um)
                    ? [.. um]
                    : [];
                MethodDefinition[] usedByMethods = usedByMethodsDict.TryGetValue(methodId, out var ubm)
                    ? [.. ubm]
                    : [];
                methodCallsBuilder.Add(methodId, new MethodCallData(method, usedMethods, usedByMethods));
            }

            MediatedCallGraph = methodCallsBuilder;
        }

        public void RemapMethodIdentifiers(IReadOnlyDictionary<string, string> oldToNew) {
            AnalysisRemap.RemapDictionaryKeysInPlace(MediatedCallGraph, oldToNew, nameof(MediatedCallGraph));
        }
    }
}
