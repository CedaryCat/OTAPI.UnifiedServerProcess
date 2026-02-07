using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis
{
    public sealed class TypeInheritanceGraph
    {
        public TypeInheritanceGraph(ModuleDefinition module) {
            _typeInheritanceChains = new(StringComparer.Ordinal);
            _directDerivedTypes = new(StringComparer.Ordinal);
            _derivedTypeTrees = new(StringComparer.Ordinal);

            // Precompute upward inheritance chains (base types + interfaces) for fast lookups.
            foreach (var type in GetTypesInInheritanceOrder(module))
                GetInheritanceTypes(type);

            // Build a "base -> direct derived types" index (classes only) for descendant tree queries.
            foreach (var type in module.GetAllTypes())
                IndexDerivedType(type);

            // Stabilize output order.
            foreach (var list in _directDerivedTypes.Values)
                list.Sort((a, b) => StringComparer.Ordinal.Compare(a.FullName, b.FullName));
        }

        /// <summary>
        /// Gets all upward reachable types for <paramref name="type"/> (itself + base types + implemented interfaces).
        /// Cached per type full name.
        /// </summary>
        public Dictionary<string, TypeDefinition> GetInheritanceTypes(TypeDefinition type) {

            if (_typeInheritanceChains.TryGetValue(type.FullName, out var cached))
                return cached;

            var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            var currentType = type;

            while (currentType != null) {
                types.TryAdd(currentType.FullName, currentType);

                foreach (var iface in currentType.Interfaces) {
                    var resolved = iface.InterfaceType.TryResolve();
                    if (resolved is null) continue;
                    types.TryAdd(resolved.FullName, resolved);
                }

                currentType = currentType.BaseType?.TryResolve();
            }

            _typeInheritanceChains.Add(type.FullName, types);
            return types;
        }

        /// <summary>
        /// Builds a derived-type tree rooted at <paramref name="root"/> (root included).
        /// Only supports class roots; interface roots are rejected to guarantee a tree.
        /// </summary>
        public TypeTreeNode GetDerivedTypeTree(TypeDefinition root) {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (root.IsInterface)
                throw new NotSupportedException("Interface roots are not supported because implementations form a DAG, not a tree.");

            return BuildDerivedTypeTree(root);
        }

        private void IndexDerivedType(TypeDefinition type) {
            // Exclude interfaces so System.Object won't incorrectly "own" all interfaces as children.
            if (type is null || type.IsInterface) return;

            var baseType = type.BaseType?.TryResolve();
            if (baseType is null) return;

            if (!_directDerivedTypes.TryGetValue(baseType.FullName, out var list)) {
                list = new List<TypeDefinition>();
                _directDerivedTypes.Add(baseType.FullName, list);
            }

            list.Add(type);
        }

        private TypeTreeNode BuildDerivedTypeTree(TypeDefinition root) {
            if (_derivedTypeTrees.TryGetValue(root.FullName, out var cached))
                return cached;

            var children = new List<TypeTreeNode>();

            if (_directDerivedTypes.TryGetValue(root.FullName, out var directChildren)) {
                foreach (var child in directChildren)
                    children.Add(BuildDerivedTypeTree(child));
            }

            var node = new TypeTreeNode(root, children);
            _derivedTypeTrees.Add(root.FullName, node);
            return node;
        }

        private readonly Dictionary<string, Dictionary<string, TypeDefinition>> _typeInheritanceChains;
        private readonly Dictionary<string, List<TypeDefinition>> _directDerivedTypes;
        private readonly Dictionary<string, TypeTreeNode> _derivedTypeTrees;

        private static List<TypeDefinition> GetTypesInInheritanceOrder(ModuleDefinition module) {
            var visited = new HashSet<TypeDefinition>();
            var sorted = new List<TypeDefinition>();

            foreach (var type in module.GetAllTypes())
                VisitType(type, visited, sorted);

            return sorted;
        }

        private static void VisitType(TypeDefinition type, HashSet<TypeDefinition> visited, List<TypeDefinition> sorted) {
            if (type is null || visited.Contains(type)) return;

            var baseType = type.BaseType?.TryResolve();
            if (baseType != null && !visited.Contains(baseType))
                VisitType(baseType, visited, sorted);

            foreach (var iface in type.Interfaces) {
                var ifaceType = iface.InterfaceType.TryResolve();
                if (ifaceType is null) continue;
                VisitType(ifaceType, visited, sorted);
            }

            if (visited.Add(type))
                sorted.Add(type);
        }
    }

    public enum TreeTraversal
    {
        PreOrder,
        PostOrder,
        BreadthFirst
    }

    /// <summary>
    /// Immutable derived-type tree node.
    /// Traversal yields TypeDefinitions in the requested order.
    /// </summary>
    public sealed class TypeTreeNode : IEnumerable<TypeDefinition>
    {
        public TypeTreeNode(TypeDefinition type, IReadOnlyList<TypeTreeNode> children) {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Children = children ?? Array.Empty<TypeTreeNode>();
        }

        public TypeDefinition Type { get; }
        public IReadOnlyList<TypeTreeNode> Children { get; }

        /// <summary>
        /// Traverses the tree and yields TypeDefinitions in the chosen order.
        /// </summary>
        public IEnumerable<TypeDefinition> Traverse(TreeTraversal order = TreeTraversal.PreOrder) => order switch {
            TreeTraversal.PreOrder => PreOrder(this),
            TreeTraversal.PostOrder => PostOrder(this),
            TreeTraversal.BreadthFirst => BreadthFirst(this),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };

        // Default enumeration is pre-order.
        public IEnumerator<TypeDefinition> GetEnumerator() => Traverse().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static IEnumerable<TypeDefinition> PreOrder(TypeTreeNode node) {
            yield return node.Type;
            foreach (var child in node.Children)
                foreach (var t in PreOrder(child))
                    yield return t;
        }

        private static IEnumerable<TypeDefinition> PostOrder(TypeTreeNode node) {
            foreach (var child in node.Children)
                foreach (var t in PostOrder(child))
                    yield return t;
            yield return node.Type;
        }

        private static IEnumerable<TypeDefinition> BreadthFirst(TypeTreeNode node) {
            var q = new Queue<TypeTreeNode>();
            q.Enqueue(node);

            while (q.Count > 0) {
                var cur = q.Dequeue();
                yield return cur.Type;

                foreach (var child in cur.Children)
                    q.Enqueue(child);
            }
        }
    }
}
