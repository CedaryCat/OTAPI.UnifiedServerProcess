using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Extensions;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

/// <summary>
/// Resolves the inheritance chain of abstract model types marked with AbstractModelAttribute.
/// </summary>
public static class AbstractModelInheritanceResolver
{
    /// <summary>
    /// Extracts the ordered chain of abstract model ancestors for a given type.
    /// The chain is ordered from root (topmost ancestor) to leaf (closest to the given type).
    /// </summary>
    /// <param name="type">The type to extract abstract model inheritance for.</param>
    /// <returns>An array of named type symbols representing the inheritance chain, ordered from root to leaf.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the type inherits from multiple independent AbstractModelAttribute marked types,
    /// or when any ancestor inherits from multiple AbstractModelAttribute marked types.
    /// </exception>
    public static INamedTypeSymbol[] ExtractInheritanceChain(INamedTypeSymbol type) {
        var abstractModelAncestors = type.GetFullInheritanceTree()
            .Where(t => t.HasAbstractModelAttribute())
            .OfType<INamedTypeSymbol>()
            .ToList();

        if (abstractModelAncestors.Count == 0) {
            return [];
        }

        // Build a graph where each node points to its AbstractModel-marked parents
        var graph = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var ancestor in abstractModelAncestors) {
            var directParents = ancestor.GetFullInheritanceTree()
                .Intersect(abstractModelAncestors, SymbolEqualityComparer.Default)
                .OfType<INamedTypeSymbol>()
                .ToList();

            graph[ancestor] = directParents;
        }

        // Find roots (nodes with no parents)
        var roots = graph.Where(kvp => !kvp.Value.Any()).Select(kvp => kvp.Key).ToList();

        // Validate: only one root allowed
        if (roots.Count > 1) {
            throw new InvalidOperationException(
                $"Type {type.Name} inherits from multiple independent AbstractModelAttribute marked types: " +
                string.Join(", ", roots.Select(r => r.Name)));
        }

        // Validate: each ancestor can only have one AbstractModel parent
        foreach (var kvp in graph) {
            if (kvp.Value.Count > 1) {
                throw new InvalidOperationException(
                    $"Type {type.Name} ancestor {kvp.Key.Name} inherits from multiple AbstractModelAttribute marked types: " +
                    string.Join(", ", kvp.Value.Select(p => p.Name)));
            }
        }

        if (roots.Count == 1) {
            // Find the leaf (closest abstract ancestor that does not appear in any other node's parents)
            var leaf = graph.Keys
                .FirstOrDefault(k => !graph.Values.SelectMany(v => v).Contains(k, SymbolEqualityComparer.Default));

            var chain = new List<INamedTypeSymbol>();
            var current = leaf;

            // Follow the parent pointer up (child -> parent), then reverse to get root to leaf order
            while (current != null) {
                chain.Add(current);
                var parent = graph[current].FirstOrDefault();
                current = parent;
            }

            chain.Reverse();
            return [.. chain];
        }

        return [];
    }
}
