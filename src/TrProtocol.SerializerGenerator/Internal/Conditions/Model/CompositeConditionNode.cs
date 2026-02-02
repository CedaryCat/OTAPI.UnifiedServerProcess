namespace TrProtocol.SerializerGenerator.Internal.Conditions.Model;

/// <summary>
/// Logical AND composite node: all child conditions must be true.
/// </summary>
public sealed record AndConditionNode : ConditionNode
{
    public IReadOnlyList<ConditionNode> Children { get; }

    public AndConditionNode(IReadOnlyList<ConditionNode> children) {
        // Flatten nested AND nodes and filter out empty conditions.
        var flattened = new List<ConditionNode>();
        foreach (var child in children) {
            if (child.IsEmpty) continue;
            if (child is AndConditionNode and) {
                flattened.AddRange(and.Children);
            }
            else {
                flattened.Add(child);
            }
        }
        Children = flattened;
    }

    public AndConditionNode(params ConditionNode[] children) : this((IReadOnlyList<ConditionNode>)children) { }

    public override string ToConditionExpression(string? parentVar, bool isSerializing) {
        if (Children.Count == 0) return "true";
        if (Children.Count == 1) return Children[0].ToConditionExpression(parentVar, isSerializing);

        var expressions = Children.Select(c => c.ToConditionExpression(parentVar, isSerializing));
        return $"({string.Join(" && ", expressions)})";
    }

    public override string GetNormalizedKey() {
        if (Children.Count == 0) return "AND:[]";
        var sortedKeys = Children.Select(c => c.GetNormalizedKey()).OrderBy(k => k);
        return $"AND:[{string.Join(",", sortedKeys)}]";
    }

    public override bool IsEmpty => Children.Count == 0;

    public override bool IsDirectionDependent => Children.Any(c => c.IsDirectionDependent);

    public override bool Contains(ConditionNode other) {
        // AND[A, B] contains AND[A, B, C] (more conditions).
        // AND[A] contains A.
        if (other is AndConditionNode otherAnd) {
            // All children of this node must exist in other.
            return Children.All(c => otherAnd.Children.Any(oc => c.Contains(oc)));
        }
        // AND[A] contains A.
        if (Children.Count == 1) {
            return Children[0].Contains(other);
        }
        return false;
    }

    /// <summary>
    /// Extracts conditions in this AND node that are not in <paramref name="subCondition"/> (set difference).
    /// </summary>
    public ConditionNode Subtract(ConditionNode subCondition) {
        if (subCondition.IsEmpty) return this;

        var subKeys = subCondition is AndConditionNode subAnd
            ? new HashSet<string>(subAnd.Children.Select(c => c.GetNormalizedKey()))
            : [subCondition.GetNormalizedKey()];

        var remaining = Children.Where(c => !subKeys.Contains(c.GetNormalizedKey())).ToList();

        if (remaining.Count == 0) return EmptyConditionNode.Instance;
        if (remaining.Count == 1) return remaining[0];
        return new AndConditionNode(remaining);
    }
}

/// <summary>
/// Logical OR composite node: any child condition being true makes the whole condition true.
/// </summary>
public sealed record OrConditionNode : ConditionNode
{
    public IReadOnlyList<ConditionNode> Children { get; }

    public OrConditionNode(IReadOnlyList<ConditionNode> children) {
        // Flatten nested OR nodes.
        var flattened = new List<ConditionNode>();
        foreach (var child in children) {
            if (child is OrConditionNode or) {
                flattened.AddRange(or.Children);
            }
            else if (!child.IsEmpty) {
                flattened.Add(child);
            }
            else {
                // An empty condition (always true) makes the whole OR true.
                Children = new List<ConditionNode> { EmptyConditionNode.Instance };
                return;
            }
        }
        Children = flattened;
    }

    public OrConditionNode(params ConditionNode[] children) : this((IReadOnlyList<ConditionNode>)children) { }

    public override string ToConditionExpression(string? parentVar, bool isSerializing) {
        if (Children.Count == 0) return "false";
        if (Children.Count == 1) return Children[0].ToConditionExpression(parentVar, isSerializing);

        var expressions = Children.Select(c => {
            var expr = c.ToConditionExpression(parentVar, isSerializing);
            // If a child condition is AND, it may need parentheses.
            return c is AndConditionNode ? expr : expr;
        });
        return $"({string.Join(" || ", expressions)})";
    }

    public override string GetNormalizedKey() {
        if (Children.Count == 0) return "OR:[]";
        var sortedKeys = Children.Select(c => c.GetNormalizedKey()).OrderBy(k => k);
        return $"OR:[{string.Join(",", sortedKeys)}]";
    }

    public override bool IsEmpty => Children.Count == 1 && Children[0].IsEmpty;

    public override bool IsDirectionDependent => Children.Any(c => c.IsDirectionDependent);
}
