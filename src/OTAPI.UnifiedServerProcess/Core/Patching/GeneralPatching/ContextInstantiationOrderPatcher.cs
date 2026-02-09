using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Adjusts instantiation order for contextualized types converted from static types. Their constructors contain logic transformed from static initialization (processed by <see cref="CctorCtxAdaptPatcher"/>),
    /// <para>which preserves original static dependency relationships. Instantiation must strictly follow dependency order using Kahn's topological sorting algorithm to ensure initialization integrity.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public class ContextInstantiationOrderPatcher(ILogger logger, MethodCallGraph callGraph, TypeInheritanceGraph typeInheritanceGraph) : GeneralPatcher(logger), IJumpSitesCacheFeature
    {
        public override string Name => nameof(ContextInstantiationOrderPatcher);

        public override void Patch(PatcherArguments arguments) {
            ModuleDefinition module = arguments.MainModule;
            TypeDefinition rootContextDef = arguments.RootContextDef;

            var contexts = arguments.ContextTypes.ToDictionary();
            var contextReferenceGraph = contexts.ToDictionary(kv => kv.Value, kv => AnalyzeInstantiationReferences(contexts, kv.Value));

            IEnumerable<ContextTypeData> order = DetermineInstantiationOrder(contextReferenceGraph)
                .Where(x => x.ContextTypeDef.DeclaringType is null && !(x.IsReusedSingleton && !x.SingletonCtorCallShouldBeMoveToRootCtor));

            MethodDefinition rootContextCtor = rootContextDef.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            rootContextCtor.Body.Instructions.Clear();

            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));

            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            rootContextCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, rootContextDef.GetField("Name")));

            foreach (ContextTypeData? context in order) {
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

            // InitializeEn adjacency list and in-degree for all nodes
            foreach (ContextTypeData type in dependencies.Keys) {
                adjacencyList[type] = [];
                inDegree[type] = 0;
            }
            // Fill adjacency list and in-degree
            foreach (KeyValuePair<ContextTypeData, ContextTypeData[]> kvp in dependencies) {
                ContextTypeData currentType = kvp.Key;
                ContextTypeData[] dependentTypes = kvp.Value;

                foreach (ContextTypeData depType in dependentTypes) {
                    adjacencyList[depType].Add(currentType);
                    inDegree[currentType]++;
                }
            }
            // Kahn's algorithm to initialize the queue
            var queue = new Queue<ContextTypeData>();
            foreach (ContextTypeData type in inDegree.Keys) {
                if (inDegree[type] == 0) {
                    queue.Enqueue(type);
                }
            }
            // Process the queue to generate a topological sort
            var result = new List<ContextTypeData>();
            while (queue.Count > 0) {
                ContextTypeData current = queue.Dequeue();
                result.Add(current);

                foreach (ContextTypeData neighbor in adjacencyList[current]) {
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
                List<List<ContextTypeData>> cycles = CycleTool.FindAllCycles(dependencies, unresolvedNodes);
            }

            return result;
        }
        public ContextTypeData[] AnalyzeInstantiationReferences(Dictionary<string, ContextTypeData> contexts, ContextTypeData context) {
            Dictionary<string, ContextTypeData> visitedContext = [];
            Dictionary<string, MethodDefinition> visitedMethod = [];
            Stack<MethodBody> works = [];
            works.Push(context.constructor.Body);

            while (works.Count > 0) {
                MethodBody body = works.Pop();
                bool callGraphRecorded = false;
                if (callGraph.MediatedCallGraph.TryGetValue(body.Method.GetIdentifier(), out MethodCallData? callData)) {
                    callGraphRecorded = true;
                    foreach (MethodReferenceData used in callData.UsedMethods) {
                        if (used.implicitCallMode is ImplicitCallMode.Delegate) {
                            continue;
                        }
                        if (used.DirectlyCalledMethod.HasBody && visitedMethod.TryAdd(used.DirectlyCalledMethod.GetIdentifier(), used.DirectlyCalledMethod)) {
                            works.Push(used.DirectlyCalledMethod.Body);
                        }
                        if (used.implicitCallMode is ImplicitCallMode.Inheritance) {
                            foreach (MethodDefinition callee in used.ImplementedMethods()) {
                                if (callee.HasBody && visitedMethod.TryAdd(callee.GetIdentifier(), callee)) {
                                    works.Push(callee.Body);
                                }
                            }
                        }
                    }
                }
                foreach (Instruction? instruction in body.Instructions) {
                    if (instruction.OpCode == OpCodes.Ldfld && contexts.TryGetValue(((FieldReference)instruction.Operand).FieldType.FullName, out ContextTypeData? referencedContext)) {
                        visitedContext.TryAdd(referencedContext.ContextTypeDef.FullName, referencedContext);
                    }
                    if (!callGraphRecorded) {
                        if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj) {
                            var methodRef = (MethodReference)instruction.Operand;
                            MethodDefinition? methodDef = methodRef.TryResolve();
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
                    if (instruction.OpCode == OpCodes.Call) {
                        var methodRef = (MethodReference)instruction.Operand;
                        if (methodRef.DeclaringType.Name is not nameof(Activator)) {
                            continue;
                        }
                        TypeReference? declaring = null;
                        if (methodRef is GenericInstanceMethod gim && gim.Name is "CreateInstance`1") {
                            declaring = gim.GenericArguments.Single();
                        }
                        else if (
                            methodRef.Parameters.Count is 1 or 2 &&
                            methodRef.Name is "CreateInstance" &&
                            methodRef.Parameters[0].ParameterType.FullName == "System.Type") {

                            Instruction ldtoken = MonoModCommon.Stack.AnalyzeParametersSources(body.Method, instruction, this.GetMethodJumpSites(body.Method))
                                .Single()
                                .ParametersSources[0].Instructions
                                .First();
                            if (ldtoken.OpCode == OpCodes.Ldtoken) {
                                declaring = (TypeReference)ldtoken.Operand;
                            }
                        }
                        if (declaring is GenericParameter gp) {
                            declaring = gp.Constraints.FirstOrDefault()?.ConstraintType;
                            TypeDefinition? def = declaring?.TryResolve();
                            if (def is not null) {
                                foreach (TypeDefinition child in typeInheritanceGraph.GetDerivedTypeTree(def)) {
                                    foreach (MethodDefinition? ctor in child.Methods.Where(x => x.IsConstructor && !x.IsStatic)) {
                                        works.Push(ctor.Body);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Remove self
            visitedContext.Remove(context.ContextTypeDef.FullName);

            return visitedContext.Values.ToArray();
        }
        static class CycleTool
        {
            public static List<List<ContextTypeData>> FindAllCycles(
                Dictionary<ContextTypeData, ContextTypeData[]> dependencies,
                List<ContextTypeData> unresolvedNodes) {

                // for cycle detection
                var allCycles = new HashSet<string>();
                var cycles = new List<List<ContextTypeData>>();

                foreach (ContextTypeData node in unresolvedNodes) {
                    var visited = new Dictionary<ContextTypeData, VisitState>();
                    var pathStack = new Stack<ContextTypeData>();

                    // InitializeEn visit state
                    foreach (ContextTypeData n in dependencies.Keys)
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

                        List<ContextTypeData> normalizedCycle = NormalizeCycle(cycleNodes);
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

                foreach (ContextTypeData neighbor in dependencies[node]) {
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
                ContextTypeData minNode = nodes.OrderBy(n => n.ContextTypeDef.Name).First();
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
