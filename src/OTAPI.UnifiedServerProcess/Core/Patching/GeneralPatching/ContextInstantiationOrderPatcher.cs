using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching {
    /// <summary>
    /// Adjusts instantiation order for contextualized types converted from static types. Their constructors contain logic transformed from static initialization (processed by <see cref="CctorCtxAdaptPatcher"/>),
    /// <para>which preserves original static dependency relationships. Instantiation must strictly follow dependency order using Kahn's topological sorting algorithm to ensure initialization integrity.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public class ContextInstantiationOrderPatcher(ILogger logger, MethodCallGraph callGraph) : GeneralPatcher(logger) {
        public override string Name => nameof(ContextInstantiationOrderPatcher);

        public override void Patch(PatcherArguments arguments) {
            var module = arguments.MainModule;
            var rootContextDef = arguments.RootContextDef;

            var contexts = arguments.ContextTypes.ToDictionary();
            var contextReferenceGraph = contexts.ToDictionary(kv => kv.Value, kv => AnalyzeInstantiationReferences(contexts, kv.Value));

            var order = DetermineInstantiationOrder(contextReferenceGraph)
                .Where(x => x.ContextTypeDef.DeclaringType is null && !(x.IsReusedSingleton && !x.SingletonCtorCallShouldBeMoveToRootCtor));

            var rootContextCtor = rootContextDef.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            rootContextCtor.Body.Instructions.Clear();

            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));

            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, rootContextDef.GetField("Name")));

            foreach (var context in order) {
                rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, context.constructor));
                rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, context.nestedChain.Single()));
            }
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        public List<ContextTypeData> DetermineInstantiationOrder(Dictionary<ContextTypeData, ContextTypeData[]> dependencies) {

            // Build adjacency list and in-degree dictionary
            var adjacencyList = new Dictionary<ContextTypeData, List<ContextTypeData>>();
            var inDegree = new Dictionary<ContextTypeData, int>();

            // Initialize adjacency list and in-degree for all nodes
            foreach (var type in dependencies.Keys) {
                adjacencyList[type] = [];
                inDegree[type] = 0;
            }
            // Fill adjacency list and in-degree
            foreach (var kvp in dependencies) {
                var currentType = kvp.Key;
                var dependentTypes = kvp.Value;

                foreach (var depType in dependentTypes) {
                    adjacencyList[depType].Add(currentType);
                    inDegree[currentType]++;
                }
            }
            // Kahn's algorithm to initialize the queue
            var queue = new Queue<ContextTypeData>();
            foreach (var type in inDegree.Keys) {
                if (inDegree[type] == 0) {
                    queue.Enqueue(type);
                }
            }
            // Process the queue to generate a topological sort
            var result = new List<ContextTypeData>();
            while (queue.Count > 0) {
                var current = queue.Dequeue();
                result.Add(current);

                foreach (var neighbor in adjacencyList[current]) {
                    inDegree[neighbor]--;

                    if (inDegree[neighbor] == 0) {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Check for cycles
            if (result.Count != dependencies.Count) {
                // find all unresolved nodes
                var unresolvedNodes = dependencies.Keys.Except(result).ToList();
                var cycles = CycleTool.FindAllCycles(dependencies, unresolvedNodes);
            }

            return result;
        }
        public ContextTypeData[] AnalyzeInstantiationReferences(Dictionary<string, ContextTypeData> contexts, ContextTypeData context) {
            Dictionary<string, ContextTypeData> visitedContext = [];
            Dictionary<string, MethodDefinition> visitedMethod = [];
            Stack<MethodBody> works = [];
            works.Push(context.constructor.Body);

            while (works.Count > 0) {
                var body = works.Pop();
                bool callGraphRecorded = false;
                if (callGraph.MediatedCallGraph.TryGetValue(body.Method.GetIdentifier(), out var callData)) {
                    callGraphRecorded = true;
                    foreach (var used in callData.UsedMethods) {
                        if(used.implicitCallMode is ImplicitCallMode.Delegate) {
                            continue;
                        }
                        if (used.DirectlyCalledMethod.HasBody && visitedMethod.TryAdd(used.DirectlyCalledMethod.GetIdentifier(), used.DirectlyCalledMethod)) {
                            works.Push(used.DirectlyCalledMethod.Body);
                        }
                        if (used.implicitCallMode is ImplicitCallMode.Inheritance) {
                            foreach (var callee in used.ImplementedMethods()) {
                                if (callee.HasBody && visitedMethod.TryAdd(callee.GetIdentifier(), callee)) {
                                    works.Push(callee.Body);
                                }
                            }
                        }
                    }
                }
                foreach (var instruction in body.Instructions) {
                    if (instruction.OpCode == OpCodes.Ldfld && contexts.TryGetValue(((FieldReference)instruction.Operand).FieldType.FullName, out var referencedContext)) {
                        visitedContext.TryAdd(referencedContext.ContextTypeDef.FullName, referencedContext);
                    }
                    if (!callGraphRecorded) {
                        if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj) {
                            var methodRef = (MethodReference)instruction.Operand;
                            var methodDef = methodRef.TryResolve();
                            if (methodDef is null) {
                                continue;
                            }
                            if (instruction.OpCode == OpCodes.Newobj && contexts.ContainsKey(methodDef.DeclaringType.FullName)) {
                                continue;
                            }
                            else if (methodDef.HasBody && visitedMethod.TryAdd(methodDef.GetIdentifier(), methodDef)) {
                                works.Push(methodDef.Body);
                            }
                        }
                    }
                }
            }

            // Remove self
            visitedContext.Remove(context.ContextTypeDef.FullName);

            return visitedContext.Values.ToArray();
        }
        static class CycleTool {
            public static List<List<ContextTypeData>> FindAllCycles(
                Dictionary<ContextTypeData, ContextTypeData[]> dependencies,
                List<ContextTypeData> unresolvedNodes) {

                // for cycle detection
                var allCycles = new HashSet<string>(); 
                var cycles = new List<List<ContextTypeData>>();

                foreach (var node in unresolvedNodes) {
                    var visited = new Dictionary<ContextTypeData, VisitState>();
                    var pathStack = new Stack<ContextTypeData>();

                    // Initialize visit state
                    foreach (var n in dependencies.Keys)
                        visited[n] = VisitState.Unvisited;

                    DFSFindAllCycles(node, dependencies, visited, pathStack, allCycles, cycles);
                }

                return cycles;
            }

            // Collect all unique cycles
            public static void DFSFindAllCycles(
                ContextTypeData node,
                Dictionary<ContextTypeData, ContextTypeData[]> dependencies,
                Dictionary<ContextTypeData, VisitState> visited,
                Stack<ContextTypeData> pathStack,
                HashSet<string> knownCycleHashes,
                List<List<ContextTypeData>> cycles) {

                if (visited[node] == VisitState.Visited)
                    return;
                if (visited[node] == VisitState.Visiting) {
                    var pathList = pathStack.Reverse().ToList();
                    int cycleStartIndex = pathList.IndexOf(node);

                    if (cycleStartIndex != -1) {
                        var cycleNodes = pathList.Skip(cycleStartIndex).ToList();
                        cycleNodes.Add(node); 

                        var normalizedCycle = NormalizeCycle(cycleNodes);
                        string hash = GetCycleHash(normalizedCycle);

                        if (!knownCycleHashes.Contains(hash)) {
                            knownCycleHashes.Add(hash);
                            cycles.Add(normalizedCycle);
                        }
                    }
                    return;
                }

                visited[node] = VisitState.Visiting;
                pathStack.Push(node);

                foreach (var neighbor in dependencies[node]) {
                    DFSFindAllCycles(neighbor, dependencies, visited, pathStack, knownCycleHashes, cycles);
                }
                visited[node] = VisitState.Visited;
                if (pathStack.Count > 0 && pathStack.Peek() == node) {
                    pathStack.Pop();
                }
            }

            // Normalize cycle: sort the cycle by the smallest node
            public static List<ContextTypeData> NormalizeCycle(List<ContextTypeData> rawCycle) {
                // Remove closed loop node (e.g. A→B→C→A becomes [A,B,C])
                var nodes = rawCycle.Take(rawCycle.Count - 1).ToList();
                // Find the smallest starting node
                var minNode = nodes.OrderBy(n => n.ContextTypeDef.Name).First();
                int startIndex = nodes.IndexOf(minNode);
                // Reorder the cycle
                var normalized = new List<ContextTypeData>();
                for (int i = startIndex; i < nodes.Count; i++)
                    normalized.Add(nodes[i]);
                for (int i = 0; i < startIndex; i++)
                    normalized.Add(nodes[i]);

                return normalized;
            }

            // Generate unique cycle hash
            public static string GetCycleHash(List<ContextTypeData> cycle) {
                return string.Join("→", cycle.Take(cycle.Count - 1).Select(n => n.ContextTypeDef.FullName));
            }

            // Format cycle display
            public static string FormatCycle(List<ContextTypeData> cycle) {
                return string.Join(" → ", cycle.Select(n => n.ContextTypeDef.FullName));
            }

            public enum VisitState { Unvisited, Visiting, Visited }
        }
    }
}
