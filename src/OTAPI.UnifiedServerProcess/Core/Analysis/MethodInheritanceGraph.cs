using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis
{
    /// <summary>
    /// Rooted directed acyclic graph for method inheritance relationships.
    /// </summary>
    /// <remarks>
    /// Enumeration is de-duplicated by method identifier.
    /// </remarks>
    public sealed class MethodInheritanceChainGraph : IEnumerable<MethodDefinition>
    {
        readonly Dictionary<MethodDefinition, MethodDefinition[]> adjacency;

        public MethodInheritanceChainGraph(
            MethodDefinition root,
            Dictionary<MethodDefinition, MethodDefinition[]> adjacency) {

            Root = root ?? throw new ArgumentNullException(nameof(root));
            this.adjacency = adjacency ?? throw new ArgumentNullException(nameof(adjacency));

            if (!this.adjacency.ContainsKey(Root)) {
                this.adjacency.Add(Root, []);
            }

            ValidateAcyclic();
        }

        public MethodDefinition Root { get; }
        public IReadOnlyDictionary<MethodDefinition, MethodDefinition[]> Adjacency => adjacency;

        /// <summary>
        /// Counts all reachable node occurrences from <see cref="Root"/> (no de-duplication).
        /// </summary>
        public int CountAllNodes() {
            int count = 0;
            Stack<MethodDefinition> work = [];
            work.Push(Root);

            while (work.Count > 0) {
                MethodDefinition current = work.Pop();
                count++;

                if (!adjacency.TryGetValue(current, out MethodDefinition[]? children)) {
                    continue;
                }

                for (int i = children.Length - 1; i >= 0; i--) {
                    work.Push(children[i]);
                }
            }

            return count;
        }

        /// <summary>
        /// Counts distinct reachable nodes from <see cref="Root"/> (de-duplicated by identifier).
        /// </summary>
        public int CountDistinctNodes() {
            int count = 0;
            foreach (MethodDefinition _ in this) {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Returns all distinct reachable vertices from <see cref="Root"/>.
        /// </summary>
        public MethodDefinition[] GetVertices() => [.. this];

        /// <summary>
        /// Returns vertices that have no outgoing edges in the reachable subgraph.
        /// </summary>
        /// <param name="requireIncomingEdges">
        /// When true, only returns vertices with in-degree &gt; 0 (excludes an isolated root).
        /// </param>
        public MethodDefinition[] GetVerticesWithNoOutgoingEdges(bool requireIncomingEdges = true) {
            (Dictionary<string, int>? inDegree, Dictionary<string, int>? outDegree, Dictionary<string, MethodDefinition>? verticesById) = BuildDegreeIndex();

            return [.. verticesById
                .Where(kv => outDegree[kv.Key] == 0 && (!requireIncomingEdges || inDegree[kv.Key] > 0))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)];
        }

        /// <summary>
        /// Alias of <see cref="GetVerticesWithNoOutgoingEdges(bool)"/>.
        /// </summary>
        public MethodDefinition[] GetLeafVertices(bool requireIncomingEdges = true) =>
            GetVerticesWithNoOutgoingEdges(requireIncomingEdges);

        public bool TryGetChildren(MethodDefinition method, out MethodDefinition[] children) =>
            adjacency.TryGetValue(method, out children!);

        public IEnumerator<MethodDefinition> GetEnumerator() => EnumerateDistinct().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        IEnumerable<MethodDefinition> EnumerateDistinct() {
            Stack<MethodDefinition> work = [];
            HashSet<string> emitted = new(StringComparer.Ordinal);
            work.Push(Root);

            while (work.Count > 0) {
                MethodDefinition current = work.Pop();
                var currentId = current.GetIdentifier();
                if (!emitted.Add(currentId)) {
                    continue;
                }

                yield return current;

                if (!adjacency.TryGetValue(current, out MethodDefinition[]? children)) {
                    continue;
                }

                for (int i = children.Length - 1; i >= 0; i--) {
                    work.Push(children[i]);
                }
            }
        }

        void ValidateAcyclic() {
            var states = new Dictionary<MethodDefinition, NodeVisitState>(ReferenceEqualityComparer.Instance);

            if (!Visit(Root)) {
                throw new InvalidOperationException($"Cyclic method inheritance chain graph detected at root '{Root.GetIdentifier()}'.");
            }

            bool Visit(MethodDefinition current) {
                if (states.TryGetValue(current, out NodeVisitState state)) {
                    return state switch {
                        NodeVisitState.Visiting => false,
                        NodeVisitState.Visited => true,
                        _ => throw new ArgumentOutOfRangeException(nameof(state))
                    };
                }

                states[current] = NodeVisitState.Visiting;
                if (adjacency.TryGetValue(current, out MethodDefinition[]? children)) {
                    foreach (MethodDefinition child in children) {
                        if (!Visit(child)) {
                            return false;
                        }
                    }
                }

                states[current] = NodeVisitState.Visited;
                return true;
            }
        }

        (Dictionary<string, int> inDegree, Dictionary<string, int> outDegree, Dictionary<string, MethodDefinition> verticesById) BuildDegreeIndex() {
            var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var outDegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var verticesById = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);

            MethodDefinition[] vertices = GetVertices();
            foreach (MethodDefinition vertex in vertices) {
                var id = vertex.GetIdentifier();
                verticesById.TryAdd(id, vertex);
                inDegree.TryAdd(id, 0);
                outDegree.TryAdd(id, 0);
            }

            foreach (MethodDefinition vertex in vertices) {
                var fromId = vertex.GetIdentifier();
                if (!adjacency.TryGetValue(vertex, out MethodDefinition[]? children)) {
                    continue;
                }

                HashSet<string> uniqueChildren = new(StringComparer.Ordinal);
                foreach (MethodDefinition child in children) {
                    var toId = child.GetIdentifier();
                    if (!verticesById.ContainsKey(toId) || !uniqueChildren.Add(toId)) {
                        continue;
                    }

                    outDegree[fromId]++;
                    inDegree[toId]++;
                }
            }

            return (inDegree, outDegree, verticesById);
        }

        enum NodeVisitState : byte
        {
            Visiting = 1,
            Visited = 2
        }
    }

    /// <summary>
    /// Builds method-implementation relationships across a set of Cecil modules, producing
    /// lookup tables that describe which methods implement/override which base or interface methods.
    /// </summary>
    /// <remarks>
    /// Keys in the dictionaries are method identifiers produced by <c>GetIdentifier</c>.
    /// Values are Cecil <see cref="MethodDefinition"/> instances representing the methods in a chain.
    /// </remarks>
    public class MethodInheritanceGraph
    {
        /// <summary>
        /// Maps a base method identifier to a rooted implementation DAG.
        /// Root is always the dictionary key method itself.
        /// </summary>
        public readonly Dictionary<string, MethodInheritanceChainGraph> RawMethodImplementationGraphs;

        /// <summary>
        /// Same as <see cref="RawMethodImplementationGraphs"/>, but descendant nodes are filtered
        /// to methods with an IL body (root is still retained).
        /// </summary>
        public readonly Dictionary<string, MethodInheritanceChainGraph> CheckedMethodImplementationGraphs;

        /// <summary>
        /// For each method identifier, a rooted inheritance DAG that traverses upward to inherited methods.
        /// Root is always the dictionary key method itself.
        /// </summary>
        public readonly Dictionary<string, MethodInheritanceChainGraph> ImmediateInheritanceGraphs;

        /// <summary>
        /// Maps a base method identifier to the full set of discovered implementing methods,
        /// including methods without bodies (e.g., abstract/external stubs).
        /// </summary>
        /// <remarks>
        /// Legacy compatibility field. Prefer <see cref="RawMethodImplementationGraphs"/>.
        /// </remarks>
        public readonly Dictionary<string, MethodDefinition[]> RawMethodImplementationChains;

        /// <summary>
        /// Same as <see cref="RawMethodImplementationChains"/>, but filtered to methods that have an IL body.
        /// This is typically used to restrict to "callable/patchable" implementations.
        /// </summary>
        /// <remarks>
        /// Legacy compatibility field. Prefer <see cref="CheckedMethodImplementationGraphs"/>.
        /// </remarks>
        public readonly Dictionary<string, MethodDefinition[]> CheckedMethodImplementationChains;

        /// <summary>
        /// For each method identifier, lists the immediate base methods it implements/overrides.
        /// </summary>
        /// <remarks>
        /// Legacy compatibility field. Prefer <see cref="ImmediateInheritanceGraphs"/>.
        /// </remarks>
        public readonly Dictionary<string, MethodDefinition[]> ImmediateInheritanceChains;

        /// <summary>
        /// Initializes a new <see cref="MethodInheritanceGraph"/> from one or more Cecil modules.
        /// </summary>
        /// <param name="modules">Modules whose types/methods will be analyzed.</param>
        public MethodInheritanceGraph(params ModuleDefinition[] modules) {
            var typedMethods = new Dictionary<string, Dictionary<string, MethodDefinition>>(StringComparer.Ordinal);
            var chains = new Dictionary<string, Dictionary<string, MethodDefinition>>(StringComparer.Ordinal);
            List<TypeDefinition> typeOrder = GetTypesInInheritanceOrder(modules);

            // Index methods per type using the "untyped" identifier to support generic instantiation matching.
            foreach (TypeDefinition type in typeOrder) {
                typedMethods[type.FullName] = type.Methods.ToDictionary(m => m.GetIdentifier(false), m => m);
            }

            // Seed chains with interface-to-implementation relationships (including through base types).
            foreach (TypeDefinition type in typeOrder) {
                ProcessInterfaces(type, typedMethods, chains);
            }

            // Invert the interface chains so we can query "method -> interface methods it satisfies".
            Dictionary<string, Dictionary<string, MethodDefinition>> interfaceInheritance = GenerateInheritanceChains(chains);

            // Remove self-links introduced by inversion (a method is not considered its own base).
            foreach (KeyValuePair<string, Dictionary<string, MethodDefinition>> kv in interfaceInheritance) {
                kv.Value.Remove(kv.Key);
            }

            // Add class/virtual override relationships on top of the interface relationships.
            foreach (TypeDefinition type in typeOrder) {
                foreach (MethodDefinition? method in type.Methods) {
                    ProcessMethod(method, interfaceInheritance, chains);
                }
            }

            Dictionary<string, Dictionary<string, MethodDefinition>> methodInheritance = GenerateInheritanceChains(chains);
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> downwardAdjacency = BuildAdjacency(methodInheritance, reverse: false);
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> upwardAdjacency = BuildAdjacency(methodInheritance, reverse: true);

            RawMethodImplementationGraphs = BuildImplementationChainGraphs(
                chains,
                downwardAdjacency,
                includeBodyOnly: false
            );

            CheckedMethodImplementationGraphs = BuildImplementationChainGraphs(
                chains,
                downwardAdjacency,
                includeBodyOnly: true
            );

            ImmediateInheritanceGraphs = BuildImmediateInheritanceChainGraphs(
                methodInheritance,
                upwardAdjacency
            );

            // Compatibility: preserve original array-based fields during migration.
            RawMethodImplementationChains = chains.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.ToArray(),
                StringComparer.Ordinal
            );

            CheckedMethodImplementationChains = chains.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.Where(m => m.Body != null).ToArray(),
                StringComparer.Ordinal
            );

            ImmediateInheritanceChains = methodInheritance.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.ToArray(),
                StringComparer.Ordinal
            );
        }

        /// <summary>
        /// Remaps all dictionary keys using an identifier mapping (old identifier -> new identifier).
        /// </summary>
        /// <param name="oldToNew">Mapping from old method identifiers to new method identifiers.</param>
        public void RemapMethodIdentifiers(IReadOnlyDictionary<string, string> oldToNew) {
            AnalysisRemap.RemapDictionaryKeysInPlace(RawMethodImplementationGraphs, oldToNew, nameof(RawMethodImplementationGraphs));
            AnalysisRemap.RemapDictionaryKeysInPlace(CheckedMethodImplementationGraphs, oldToNew, nameof(CheckedMethodImplementationGraphs));
            AnalysisRemap.RemapDictionaryKeysInPlace(ImmediateInheritanceGraphs, oldToNew, nameof(ImmediateInheritanceGraphs));

            AnalysisRemap.RemapDictionaryKeysInPlace(RawMethodImplementationChains, oldToNew, nameof(RawMethodImplementationChains));
            AnalysisRemap.RemapDictionaryKeysInPlace(CheckedMethodImplementationChains, oldToNew, nameof(CheckedMethodImplementationChains));
            AnalysisRemap.RemapDictionaryKeysInPlace(ImmediateInheritanceChains, oldToNew, nameof(ImmediateInheritanceChains));
        }

        static Dictionary<string, MethodInheritanceChainGraph> BuildImplementationChainGraphs(
            Dictionary<string, Dictionary<string, MethodDefinition>> chains,
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> adjacency,
            bool includeBodyOnly) {

            var result = new Dictionary<string, MethodInheritanceChainGraph>(chains.Count, StringComparer.Ordinal);

            foreach ((string? rootIdentifier, Dictionary<string, MethodDefinition>? methods) in chains) {
                if (!TryGetRootMethod(rootIdentifier, methods, out MethodDefinition? rootMethod)) {
                    continue;
                }

                HashSet<string> allowed = includeBodyOnly
                    ? methods.Values
                        .Where(m => m.Body != null)
                        .Select(m => m.GetIdentifier())
                        .ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(methods.Keys, StringComparer.Ordinal);

                allowed.Add(rootIdentifier);
                result.Add(rootIdentifier, CreateRootedGraph(rootMethod, adjacency, allowed));
            }

            return result;
        }

        static Dictionary<string, MethodInheritanceChainGraph> BuildImmediateInheritanceChainGraphs(
            Dictionary<string, Dictionary<string, MethodDefinition>> methodInheritance,
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> adjacency) {

            var result = new Dictionary<string, MethodInheritanceChainGraph>(methodInheritance.Count, StringComparer.Ordinal);

            foreach ((string? rootIdentifier, Dictionary<string, MethodDefinition>? inheritances) in methodInheritance) {
                if (!TryGetRootMethod(rootIdentifier, inheritances, out MethodDefinition? rootMethod)) {
                    continue;
                }

                HashSet<string> allowed = new(inheritances.Keys, StringComparer.Ordinal) { rootIdentifier };
                result.Add(rootIdentifier, CreateRootedGraph(rootMethod, adjacency, allowed));
            }

            return result;
        }

        static MethodInheritanceChainGraph CreateRootedGraph(
            MethodDefinition root,
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> adjacency,
            HashSet<string> allowed) {

            var visitedIds = new HashSet<string>(StringComparer.Ordinal);
            var resultAdjacency = new Dictionary<MethodDefinition, MethodDefinition[]>(ReferenceEqualityComparer.Instance);
            Stack<MethodDefinition> work = [];
            work.Push(root);

            while (work.Count > 0) {
                MethodDefinition current = work.Pop();
                var currentId = current.GetIdentifier();
                if (!allowed.Contains(currentId) || !visitedIds.Add(currentId)) {
                    continue;
                }

                MethodDefinition[] children = [];
                if (adjacency.TryGetValue(current, out HashSet<MethodDefinition>? nextNodes)) {
                    Dictionary<string, MethodDefinition> uniqueChildren = new(StringComparer.Ordinal);
                    foreach (MethodDefinition next in nextNodes) {
                        var nextId = next.GetIdentifier();
                        if (!allowed.Contains(nextId)) {
                            continue;
                        }
                        uniqueChildren.TryAdd(nextId, next);
                    }

                    children = [.. uniqueChildren
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => kv.Value)];
                }

                resultAdjacency[current] = children;
                for (int i = children.Length - 1; i >= 0; i--) {
                    work.Push(children[i]);
                }
            }

            if (!resultAdjacency.ContainsKey(root)) {
                resultAdjacency.Add(root, []);
            }

            return new MethodInheritanceChainGraph(root, resultAdjacency);
        }

        static Dictionary<MethodDefinition, HashSet<MethodDefinition>> BuildAdjacency(
            Dictionary<string, Dictionary<string, MethodDefinition>> methodInheritance,
            bool reverse) {

            var result = new Dictionary<MethodDefinition, HashSet<MethodDefinition>>(ReferenceEqualityComparer.Instance);

            foreach ((string? methodIdentifier, Dictionary<string, MethodDefinition>? inheritances) in methodInheritance) {
                if (!TryGetRootMethod(methodIdentifier, inheritances, out MethodDefinition? method)) {
                    continue;
                }

                foreach ((string? baseIdentifier, MethodDefinition? baseMethod) in inheritances) {
                    if (string.Equals(methodIdentifier, baseIdentifier, StringComparison.Ordinal)) {
                        continue;
                    }

                    if (reverse) {
                        AddEdge(result, method, baseMethod);
                    }
                    else {
                        AddEdge(result, baseMethod, method);
                    }
                }
            }

            return result;
        }

        static bool TryGetRootMethod(
            string rootIdentifier,
            Dictionary<string, MethodDefinition> methods,
            out MethodDefinition rootMethod) {

            if (methods.TryGetValue(rootIdentifier, out rootMethod!)) {
                return true;
            }

            rootMethod = methods.Values.FirstOrDefault(m => m.GetIdentifier() == rootIdentifier)!;
            return rootMethod is not null;
        }

        static void AddEdge(
            Dictionary<MethodDefinition, HashSet<MethodDefinition>> adjacency,
            MethodDefinition from,
            MethodDefinition to) {

            if (!adjacency.TryGetValue(from, out HashSet<MethodDefinition>? children)) {
                adjacency.Add(from, children = new HashSet<MethodDefinition>(ReferenceEqualityComparer.Instance));
            }

            children.Add(to);
        }

        /// <summary>
        /// Inverts a base-to-implementations map into an implementation-to-bases map.
        /// </summary>
        /// <param name="chains">
        /// A dictionary keyed by base method identifier, where each value is a dictionary containing the base
        /// method entry and any discovered implementations keyed by their identifiers.
        /// </param>
        /// <returns>
        /// A dictionary keyed by implementation method identifier, where each value contains immediate base method
        /// identifiers mapped to their <see cref="MethodDefinition"/>.
        /// </returns>
        private static Dictionary<string, Dictionary<string, MethodDefinition>> GenerateInheritanceChains(
            Dictionary<string, Dictionary<string, MethodDefinition>> chains) {
            Dictionary<string, Dictionary<string, MethodDefinition>> immediateInheritanceChains = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, MethodDefinition>> kv in chains) {
                var baseMethodId = kv.Key;
                Dictionary<string, MethodDefinition> implementations = kv.Value;
                MethodDefinition baseMethod = implementations[baseMethodId];

                foreach (KeyValuePair<string, MethodDefinition> implementation in implementations) {
                    if (!immediateInheritanceChains.TryGetValue(implementation.Key, out Dictionary<string, MethodDefinition>? inheritance)) {
                        immediateInheritanceChains.Add(implementation.Key, inheritance = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal));
                    }

                    // "implementation -> base" edge
                    inheritance.TryAdd(baseMethodId, baseMethod);
                }
            }

            return immediateInheritanceChains;
        }

        /// <summary>
        /// Populates interface implementation chains for a given type by walking its declared interfaces and
        /// searching up the type hierarchy for matching implementations.
        /// </summary>
        /// <param name="type">The type being processed.</param>
        /// <param name="typedMethods">
        /// A per-type lookup of methods keyed by an identifier that ignores declaring type instantiation details,
        /// enabling matching against instantiated interface method signatures.
        /// </param>
        /// <param name="chains">The accumulating base-to-implementations map.</param>
        private static void ProcessInterfaces(
            TypeDefinition type,
            Dictionary<string, Dictionary<string, MethodDefinition>> typedMethods,
            Dictionary<string, Dictionary<string, MethodDefinition>> chains) {
            foreach (InterfaceImplementation? interfaceImpl in type.Interfaces) {
                TypeDefinition interfaceDef = interfaceImpl.InterfaceType.Resolve();

                foreach (MethodDefinition? interfaceMethod in interfaceDef.Methods) {
                    // Instantiate the interface method against the concrete interface type (handles generics).
                    MethodReference typed = MonoModCommon.Structure.CreateInstantiatedMethod(interfaceMethod, interfaceImpl.InterfaceType);

                    // Walk up the base chain to account for implementations inherited from base classes.
                    TypeDefinition? currentType = type;
                    while (currentType is not null) {
                        Dictionary<string, MethodDefinition> methods = typedMethods[currentType.FullName];

                        if (methods.TryGetValue(typed.GetIdentifier(false), out MethodDefinition? impl)) {
                            var interfaceIdentifier = interfaceMethod.GetIdentifier();
                            if (!chains.TryGetValue(interfaceIdentifier, out Dictionary<string, MethodDefinition>? interfaceChain)) {
                                chains.Add(interfaceIdentifier, interfaceChain = new(StringComparer.Ordinal) { { interfaceIdentifier, interfaceMethod } });
                            }

                            // "interface method -> implementing method" edge
                            interfaceChain.TryAdd(impl.GetIdentifier(), impl);
                        }

                        currentType = currentType.BaseType?.TryResolve();
                    }
                }
            }
        }

        /// <summary>
        /// Ensures the method exists in the chain set, then registers it as an implementation of each
        /// immediate base method it overrides/implements.
        /// </summary>
        /// <param name="method">The method to register.</param>
        /// <param name="interfaceInheritance">
        /// A lookup keyed by method identifier, mapping to interface methods that the method is considered to satisfy.
        /// </param>
        /// <param name="chains">The accumulating base-to-implementations map.</param>
        private static void ProcessMethod(
            MethodDefinition method,
            Dictionary<string, Dictionary<string, MethodDefinition>> interfaceInheritance,
            Dictionary<string, Dictionary<string, MethodDefinition>> chains) {
            var identifier = method.GetIdentifier();

            if (!chains.ContainsKey(identifier)) {
                chains.Add(identifier, new(StringComparer.Ordinal) { { identifier, method } });
            }

            foreach (MethodDefinition baseMethod in GetImmediateBaseMethods(method, interfaceInheritance)) {
                var baseIdentifier = baseMethod.GetIdentifier();
                if (!chains.TryGetValue(baseIdentifier, out Dictionary<string, MethodDefinition>? baseChain)) {
                    chains.Add(baseIdentifier, baseChain = new(StringComparer.Ordinal) { { baseIdentifier, baseMethod } });
                }

                // "base method -> overriding/implementing method" edge
                baseChain.TryAdd(identifier, method);
            }
        }

        /// <summary>
        /// Returns the set of methods that are immediately implemented or overridden by <paramref name="method"/>,
        /// excluding <paramref name="method"/> itself.
        /// </summary>
        /// <param name="method">The method whose immediate bases will be discovered.</param>
        /// <param name="interfaceInheritance">
        /// A lookup of method identifier to interface methods satisfied by that method identifier.
        /// </param>
        /// <returns>
        /// Immediate base methods, including resolved explicit overrides, matching virtual base methods, and relevant interface methods.
        /// </returns>
        private static MethodDefinition[] GetImmediateBaseMethods(
            MethodDefinition method,
            Dictionary<string, Dictionary<string, MethodDefinition>> interfaceInheritance) {
            HashSet<MethodDefinition> result = [];

            // Explicit interface method overrides / explicit virtual overrides.
            foreach (MethodReference? ov in method.Overrides) {
                MethodDefinition? methodDef = ov.TryResolve();
                if (methodDef is null) {
                    continue;
                }
                result.Add(methodDef);
            }

            TypeReference currentType = method.DeclaringType;
            while (currentType != null) {
                TypeDefinition? resolved = currentType.TryResolve();
                if (resolved is null) {
                    break;
                }

                foreach (MethodDefinition? currentMethod in resolved.Methods) {
                    // Compare using an identifier that ignores instantiation details so generics match consistently.
                    MethodReference typed = MonoModCommon.Structure.CreateInstantiatedMethod(currentMethod, currentType);

                    if (typed.GetIdentifier(false) != method.GetIdentifier(false)) {
                        continue;
                    }

                    // Attach interface base methods for this slot (filtered to actual interface declarations).
                    if (interfaceInheritance.TryGetValue(currentMethod.GetIdentifier(), out Dictionary<string, MethodDefinition>? interfaceMethods)) {
                        foreach (MethodDefinition interfaceMethod in interfaceMethods.Values) {
                            if (!interfaceMethod.DeclaringType.IsInterface) {
                                continue;
                            }
                            result.Add(interfaceMethod);
                        }
                    }

                    // Non-virtual methods do not participate in inheritance chains.
                    if (!currentMethod.IsVirtual) {
                        continue;
                    }

                    // Only treat base-type occurrences as bases; avoid adding the method itself.
                    if (currentType != method.DeclaringType) {
                        result.Add(currentMethod);
                    }
                }

                currentType = resolved.BaseType;
            }

            return result.ToArray();
        }

        /// <summary>
        /// Produces a list of all types across the provided modules ordered such that base classes appear before derived classes.
        /// </summary>
        /// <param name="modules">Modules to scan for types.</param>
        /// <returns>A list of types in base-to-derived visitation order.</returns>
        private static List<TypeDefinition> GetTypesInInheritanceOrder(params ModuleDefinition[] modules) {
            var allTypes = new HashSet<TypeDefinition>();
            var sorted = new List<TypeDefinition>();

            foreach (ModuleDefinition module in modules) {
                foreach (TypeDefinition? type in module.GetAllTypes()) {
                    VisitType(type, allTypes, sorted);
                }
            }

            return sorted;
        }

        /// <summary>
        /// Depth-first visits a type's base chain to ensure bases are added before the type itself.
        /// </summary>
        /// <param name="type">The type to visit.</param>
        /// <param name="visited">Set used to prevent revisiting types.</param>
        /// <param name="sorted">Output list that preserves base-before-derived ordering.</param>
        private static void VisitType(
            TypeDefinition type,
            HashSet<TypeDefinition> visited,
            List<TypeDefinition> sorted) {
            if (type is null || visited.Contains(type)) return;

            TypeDefinition? baseType = type.BaseType?.TryResolve();
            if (baseType != null && !visited.Contains(baseType)) {
                VisitType(baseType, visited, sorted);
            }

            if (visited.Add(type)) {
                sorted.Add(type);
            }
        }
    }
}
