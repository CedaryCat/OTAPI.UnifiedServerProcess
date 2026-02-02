using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Conditions.Model;
using TrProtocol.SerializerGenerator.Internal.Conditions.Parsing;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Conditions.Analysis;

/// <summary>
/// Condition tree builder that builds a complete condition tree from member attributes.
/// </summary>
public static class ConditionTreeBuilder
{
    /// <summary>
    /// Builds a member's condition tree.
    /// Handles the OR relation between different AttributeLists and the AND relation within the same AttributeList.
    /// </summary>
    /// <param name="member">The member serialization context.</param>
    /// <param name="modelSym">The model type symbol.</param>
    /// <param name="indexVariable">The array index variable name (optional).</param>
    /// <returns>The built condition tree.</returns>
    public static ConditionNode BuildConditionTree(
        SerializationExpandContext member,
        INamedTypeSymbol modelSym,
        string? indexVariable = null) {
        // Array element conditions (ConditionArray) are opted-in via indexVariable.
        if (indexVariable != null) return BuildArrayConditionTree(member, modelSym, indexVariable);

        // Regular member conditions.
        return BuildMemberConditionTree(member, modelSym);
    }

    /// <summary>
    /// Builds a condition tree for a regular member (non-array round).
    /// </summary>
    private static ConditionNode BuildMemberConditionTree(
        SerializationExpandContext member,
        INamedTypeSymbol modelSym) {
        var orConditions = new List<ConditionNode>();

        // Iterate each AttributeList (different AttributeLists are OR'ed together).
        foreach (var attrList in member.MemberDeclaration.AttributeLists) {
            var andConditions = new List<ConditionNode>();

            // Iterate attributes within the AttributeList (attributes within the same list are AND'ed together).
            foreach (var attr in attrList.Attributes) {
                var conditionNode = ConditionAttributeParser.ParseAttribute(attr, member, modelSym, modelSym.Name);
                if (conditionNode != null) {
                    andConditions.Add(conditionNode);
                }
            }

            if (andConditions.Count == 1) {
                orConditions.Add(andConditions[0]);
            }
            else if (andConditions.Count > 1) {
                orConditions.Add(new AndConditionNode(andConditions));
            }
        }

        // If there are no conditions, return an empty condition.
        if (orConditions.Count == 0) {
            return EmptyConditionNode.Instance;
        }

        // Separate side conditions (C2SOnly/S2COnly) from other conditions.
        var sideCondition = orConditions.OfType<SideConditionNode>().FirstOrDefault();
        var otherConditions = orConditions.Where(c => c is not SideConditionNode).ToList();

        // Build the final condition tree.
        ConditionNode result;
        if (otherConditions.Count == 0) {
            result = EmptyConditionNode.Instance;
        }
        else if (otherConditions.Count == 1) {
            result = otherConditions[0];
        }
        else {
            result = new OrConditionNode(otherConditions);
        }

        // AND the side condition with the other conditions.
        if (sideCondition != null) {
            if (result.IsEmpty) {
                result = sideCondition;
            }
            else {
                result = new AndConditionNode(sideCondition, result);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a condition tree for array elements (ConditionArray).
    /// </summary>
    private static ConditionNode BuildArrayConditionTree(
        SerializationExpandContext member,
        INamedTypeSymbol modelSym,
        string? indexVariable) {
        if (indexVariable == null) return EmptyConditionNode.Instance;

        foreach (var attr in member.Attributes) {
            if (!attr.AttributeMatch<TrProtocol.Attributes.ConditionArrayAttribute>()) {
                continue;
            }
            var node = ConditionAttributeParser.ParseAttribute(attr, member, modelSym, modelSym.Name, indexVariable);
            if (node is ArrayIndexConditionNode) {
                return node;
            }
        }

        return EmptyConditionNode.Instance;
    }

    /// <summary>
    /// Determines whether a conditional member must be declared nullable.
    /// </summary>
    public static bool RequiresNullableType(ConditionNode condition, ITypeSymbol memberType) {
        if (condition.IsEmpty) return false;
        return !memberType.IsUnmanagedType && memberType.NullableAnnotation != NullableAnnotation.Annotated;
    }

    /// <summary>
    /// Extracts a valid side-specific condition (C2SOnly/S2COnly) from a tree, if present.
    /// </summary>
    public static SideConditionNode? ExtractSideCondition(ConditionNode condition) {
        if (condition is SideConditionNode side) {
            return side;
        }
        if (condition is AndConditionNode and) {
            return and.Children.OfType<SideConditionNode>().FirstOrDefault();
        }
        return null;
    }
}
