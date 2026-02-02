using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess
{
    /// <summary>
    /// A precomputed SCC index over a "type flow graph" (type -> referenced type),
    /// used to summarize recursive object graphs and guarantee convergence of tracing.
    /// </summary>
    public sealed class TypeFlowSccIndex
    {
        private readonly ImmutableDictionary<string, int> _typeToSccId;
        private readonly ImmutableHashSet<int> _recursiveSccIds;
        private readonly ImmutableDictionary<int, ImmutableHashSet<string>> _sccMembers;

        private TypeFlowSccIndex(
            ImmutableDictionary<string, int> typeToSccId,
            ImmutableHashSet<int> recursiveSccIds,
            ImmutableDictionary<int, ImmutableHashSet<string>> sccMembers) {
            _typeToSccId = typeToSccId;
            _recursiveSccIds = recursiveSccIds;
            _sccMembers = sccMembers;
        }

        public static TypeFlowSccIndex Build(ModuleDefinition module) {
            if (module is null) throw new ArgumentNullException(nameof(module));

            var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var selfEdges = new HashSet<string>(StringComparer.Ordinal);

            static string GetKey(TypeReference type) => NormalizeType(type).FullName;

            void EnsureNode(string typeKey) {
                if (!graph.ContainsKey(typeKey)) {
                    graph[typeKey] = new HashSet<string>(StringComparer.Ordinal);
                }
            }

            void AddEdge(TypeReference from, TypeReference to) {
                var fromKey = GetKey(from);
                var toKey = GetKey(to);
                EnsureNode(fromKey);
                EnsureNode(toKey);
                graph[fromKey].Add(toKey);
                if (fromKey == toKey) {
                    selfEdges.Add(fromKey);
                }
            }

            foreach (var type in module.GetAllTypes()) {
                EnsureNode(GetKey(type));

                foreach (var field in type.Fields) {
                    if (field.IsStatic) continue;
                    var fieldType = field.FieldType;
                    if (fieldType.IsTruelyValueType()) continue;

                    AddEdge(type, fieldType);

                    if (TryGetElementType(fieldType, out var elementType) && !elementType.IsTruelyValueType()) {
                        AddEdge(fieldType, elementType);
                    }
                }
            }

            var (sccIdByNode, membersByScc) = ComputeScc(graph);

            var recursive = new HashSet<int>();
            foreach (var (sccId, members) in membersByScc) {
                if (members.Count > 1) {
                    recursive.Add(sccId);
                    continue;
                }

                var only = members[0];
                if (selfEdges.Contains(only)) {
                    recursive.Add(sccId);
                }
            }

            var membersBuilder = ImmutableDictionary.CreateBuilder<int, ImmutableHashSet<string>>();
            foreach (var kv in membersByScc) {
                membersBuilder[kv.Key] = kv.Value.ToImmutableHashSet(StringComparer.Ordinal);
            }

            return new TypeFlowSccIndex(
                sccIdByNode.ToImmutableDictionary(StringComparer.Ordinal),
                recursive.ToImmutableHashSet(),
                membersBuilder.ToImmutable()
            );
        }

        public bool TryGetSccId(TypeReference type, out int sccId) {
            if (type is null) {
                sccId = default;
                return false;
            }
            return _typeToSccId.TryGetValue(NormalizeType(type).FullName, out sccId);
        }

        /// <summary>
        /// Like <see cref="TryGetSccId(TypeReference, out int)"/>, but if the exact type is absent from the index
        /// (e.g., because it only appears in method bodies), walk base types until a known SCC node is found.
        /// </summary>
        public bool TryGetSccIdIncludingBaseTypes(TypeReference type, out int sccId) {
            if (TryGetSccId(type, out sccId)) {
                return true;
            }

            var current = NormalizeType(type);
            var def = current.TryResolve();

            while (def?.BaseType is not null) {
                var baseType = NormalizeType(def.BaseType);
                if (_typeToSccId.TryGetValue(baseType.FullName, out sccId)) {
                    return true;
                }
                def = baseType.TryResolve();
            }

            sccId = default;
            return false;
        }

        /// <summary>
        /// Finds a recursive SCC id for <paramref name="type"/> by walking base types.
        /// This is useful when the type-flow graph doesn't include inherited instance fields,
        /// so a derived type may have a non-recursive SCC while its base type is recursive.
        /// </summary>
        public bool TryGetRecursiveSccIdIncludingBaseTypes(TypeReference type, out int sccId) {
            if (type is null) {
                sccId = default;
                return false;
            }

            // Prefer a recursive SCC on the exact type first.
            if (TryGetSccId(type, out sccId) && IsRecursiveScc(sccId)) {
                return true;
            }

            var current = NormalizeType(type);
            var def = current.TryResolve();

            while (def?.BaseType is not null) {
                var baseType = NormalizeType(def.BaseType);
                if (_typeToSccId.TryGetValue(baseType.FullName, out sccId) && IsRecursiveScc(sccId)) {
                    return true;
                }
                def = baseType.TryResolve();
            }

            // Fall back to any SCC (possibly non-recursive) if needed.
            return TryGetSccIdIncludingBaseTypes(type, out sccId);
        }

        public bool IsRecursiveScc(int sccId) => _recursiveSccIds.Contains(sccId);

        public bool IsInScc(TypeReference type, int sccId) =>
            type is not null
            && _sccMembers.TryGetValue(sccId, out var members)
            && members.Contains(NormalizeType(type).FullName);

        /// <summary>
        /// Like <see cref="IsInScc(TypeReference, int)"/>, but treats a derived type as "in" the SCC
        /// if any of its base types is a member. This is used to make SCC loop summarization robust
        /// across inheritance boundaries.
        /// </summary>
        public bool IsInSccIncludingBaseTypes(TypeReference type, int sccId) {
            if (IsInScc(type, sccId)) {
                return true;
            }

            var current = NormalizeType(type);
            var def = current.TryResolve();

            while (def?.BaseType is not null) {
                var baseType = NormalizeType(def.BaseType);
                if (IsInScc(baseType, sccId)) {
                    return true;
                }
                def = baseType.TryResolve();
            }

            return false;
        }

        private static bool TryGetElementType(TypeReference maybeCollection, out TypeReference elementType) {
            elementType = null!;

            maybeCollection = NormalizeType(maybeCollection);

            if (maybeCollection is ArrayType arrayType) {
                elementType = arrayType.ElementType;
                return true;
            }

            if (maybeCollection is not GenericInstanceType generic) {
                return false;
            }

            if (generic.GenericArguments.Count == 0) {
                return false;
            }

            var elementFullName = generic.ElementType.FullName;

            if (elementFullName == typeof(List<>).FullName
                || elementFullName == typeof(IList<>).FullName
                || elementFullName == typeof(ICollection<>).FullName
                || elementFullName == typeof(IEnumerable<>).FullName
                || elementFullName == typeof(ISet<>).FullName
                || elementFullName == typeof(Queue<>).FullName
                || elementFullName == typeof(Stack<>).FullName) {
                elementType = generic.GenericArguments[0];
                return true;
            }

            if (elementFullName == typeof(Dictionary<,>).FullName && generic.GenericArguments.Count >= 2) {
                elementType = generic.GenericArguments[1];
                return true;
            }

            return false;
        }

        private static TypeReference NormalizeType(TypeReference type) {
            while (type is TypeSpecification spec && type is not ArrayType && type is not GenericInstanceType && spec.ElementType is not null) {
                type = spec.ElementType;
            }
            return type;
        }

        private static (Dictionary<string, int> sccIdByNode, Dictionary<int, List<string>> membersByScc) ComputeScc(
            Dictionary<string, HashSet<string>> graph) {

            int index = 0;
            var stack = new Stack<string>();
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var lowLink = new Dictionary<string, int>(StringComparer.Ordinal);

            int nextSccId = 0;
            var sccIdByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            var membersByScc = new Dictionary<int, List<string>>();

            void StrongConnect(string v) {
                indices[v] = index;
                lowLink[v] = index;
                index++;

                stack.Push(v);
                onStack.Add(v);

                if (graph.TryGetValue(v, out var neighbors)) {
                    foreach (var w in neighbors) {
                        if (!indices.ContainsKey(w)) {
                            StrongConnect(w);
                            lowLink[v] = Math.Min(lowLink[v], lowLink[w]);
                        }
                        else if (onStack.Contains(w)) {
                            lowLink[v] = Math.Min(lowLink[v], indices[w]);
                        }
                    }
                }

                if (lowLink[v] == indices[v]) {
                    var members = new List<string>();
                    while (stack.Count > 0) {
                        var w = stack.Pop();
                        onStack.Remove(w);
                        members.Add(w);
                        sccIdByNode[w] = nextSccId;
                        if (w == v) break;
                    }
                    membersByScc[nextSccId] = members;
                    nextSccId++;
                }
            }

            foreach (var node in graph.Keys) {
                if (!indices.ContainsKey(node)) {
                    StrongConnect(node);
                }
            }

            return (sccIdByNode, membersByScc);
        }
    }
}
