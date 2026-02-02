using TrProtocol.SerializerGenerator.Internal.Conditions.Model;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Conditions.Optimization;

/// <summary>
/// Represents a conditional block that contains a sequence of members sharing the same condition.
/// </summary>
public class ConditionBlock
{
    /// <summary>The condition for this block.</summary>
    public ConditionNode Condition { get; set; }

    /// <summary>The members contained in this block.</summary>
    public List<MemberInBlock> Members { get; } = [];

    /// <summary>Nested child blocks (condition containment relationship).</summary>
    public List<ConditionBlock> NestedBlocks { get; } = [];

    public ConditionBlock(ConditionNode condition) {
        Condition = condition;
    }
}

/// <summary>
/// Member info within a condition block.
/// </summary>
public class MemberInBlock
{
    /// <summary>The member context.</summary>
    public SerializationExpandContext Member { get; }

    /// <summary>The additional condition relative to the current block condition (set difference).</summary>
    public ConditionNode AdditionalCondition { get; set; }

    /// <summary>The parent variable name.</summary>
    public string? ParentVar { get; }

    public MemberInBlock(SerializationExpandContext member, ConditionNode additionalCondition, string? parentVar) {
        Member = member;
        AdditionalCondition = additionalCondition;
        ParentVar = parentVar;
    }
}

/// <summary>
/// Adjacent-field condition merger that optimizes condition checks for contiguous members.
/// </summary>
public static class AdjacentFieldMerger
{
    /// <summary>
    /// Analyzes the member sequence and merges adjacent identical conditions or conditions with containment relationships.
    /// </summary>
    /// <param name="members">Sequence of members and their conditions.</param>
    /// <returns>Optimized list of condition blocks.</returns>
    public static List<ConditionBlock> MergeAdjacentConditions(
        IEnumerable<(SerializationExpandContext member, ConditionNode condition, string? parentVar)> members) {
        var blocks = new List<ConditionBlock>();
        ConditionBlock? currentBlock = null;

        foreach (var (member, condition, parentVar) in members) {
            if (condition.IsEmpty) {
                // Unconditional member: close the current block and emit an unconditional block.
                if (currentBlock != null) {
                    blocks.Add(currentBlock);
                    currentBlock = null;
                }

                // Create an empty-condition block.
                var emptyBlock = new ConditionBlock(EmptyConditionNode.Instance);
                emptyBlock.Members.Add(new MemberInBlock(member, EmptyConditionNode.Instance, parentVar));
                blocks.Add(emptyBlock);
                continue;
            }

            // Check whether we can merge into the current block.
            if (currentBlock != null) {
                var keyCurrent = currentBlock.Condition.GetNormalizedKey();
                var keyNew = condition.GetNormalizedKey();

                // 1) Identical conditions: merge directly.
                if (keyCurrent == keyNew) {
                    currentBlock.Members.Add(new MemberInBlock(member, EmptyConditionNode.Instance, parentVar));
                    continue;
                }

                // 2) AND containment optimization (Plan A): only handle AND containment; do not expand OR.
                if (TryGetConjunctiveAtomMap(currentBlock.Condition, out var currentAtomsByKey)
                    && TryGetConjunctiveAtomMap(condition, out var newAtomsByKey)) {
                    var currentKeys = new HashSet<string>(currentAtomsByKey.Keys);
                    var newKeys = new HashSet<string>(newAtomsByKey.Keys);

                    // current ⊆ new: the new condition is stronger -> add extra checks (new - current).
                    if (currentKeys.IsSubsetOf(newKeys)) {
                        var additionalForNew = BuildConjunctiveCondition(
                            newKeys.Except(currentKeys)
                                .OrderBy(k => k)
                                .Select(k => newAtomsByKey[k]));

                        currentBlock.Members.Add(new MemberInBlock(member, additionalForNew, parentVar));
                        continue;
                    }

                    // new ⊆ current: the new condition is weaker -> widen the block and add extra checks for existing members (current - new).
                    if (newKeys.IsSubsetOf(currentKeys)) {
                        var additionalForOld = BuildConjunctiveCondition(
                            currentKeys.Except(newKeys)
                                .OrderBy(k => k)
                                .Select(k => currentAtomsByKey[k]));

                        currentBlock.Condition = condition;
                        foreach (var existing in currentBlock.Members) {
                            existing.AdditionalCondition = CombineConditions(existing.AdditionalCondition, additionalForOld);
                        }

                        currentBlock.Members.Add(new MemberInBlock(member, EmptyConditionNode.Instance, parentVar));
                        continue;
                    }

                    // 2.5) AND common-factor extraction: a&&b and a&&c -> extract a as the block condition and keep the remainder per member.
                    var intersectionKeys = new HashSet<string>(currentKeys);
                    intersectionKeys.IntersectWith(newKeys);
                    if (intersectionKeys.Count > 0) {
                        var extractedCondition = BuildConjunctiveCondition(
                            intersectionKeys.OrderBy(k => k).Select(k => currentAtomsByKey[k]));

                        var additionalForExisting = BuildConjunctiveCondition(
                            currentKeys.Except(intersectionKeys)
                                .OrderBy(k => k)
                                .Select(k => currentAtomsByKey[k]));

                        currentBlock.Condition = extractedCondition;
                        foreach (var existing in currentBlock.Members) {
                            existing.AdditionalCondition = CombineConditions(existing.AdditionalCondition, additionalForExisting);
                        }

                        var additionalForNew = BuildConjunctiveCondition(
                            newKeys.Except(intersectionKeys)
                                .OrderBy(k => k)
                                .Select(k => newAtomsByKey[k]));

                        currentBlock.Members.Add(new MemberInBlock(member, additionalForNew, parentVar));
                        continue;
                    }
                }

                // 3) Unrelated conditions: start a new block.
                blocks.Add(currentBlock);
                currentBlock = null;
            }

            // Start a new block.
            currentBlock = new ConditionBlock(condition);
            currentBlock.Members.Add(new MemberInBlock(member, EmptyConditionNode.Instance, parentVar));
        }

        // Append the last block.
        if (currentBlock != null) {
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    /// <summary>
    /// Tries to parse a condition into a set of AND atomic conditions (handles nested AND, but not OR).
    /// </summary>
    private static bool TryGetConjunctiveAtomMap(ConditionNode node, out Dictionary<string, ConditionNode> atomsByKey) {
        atomsByKey = [];

        if (!TryGetConjunctiveAtoms(node, out var atoms)) {
            return false;
        }

        foreach (var atom in atoms) {
            if (atom.IsEmpty) {
                continue;
            }

            var key = atom.GetNormalizedKey();
            if (!atomsByKey.ContainsKey(key)) {
                atomsByKey.Add(key, atom);
            }
        }

        return true;
    }

    /// <summary>
    /// Tries to flatten <paramref name="node"/> into a list of AND atomic conditions.
    /// If an OR is encountered, returns false (containment optimization is not applied).
    /// </summary>
    private static bool TryGetConjunctiveAtoms(ConditionNode node, out List<ConditionNode> atoms) {
        atoms = [];

        if (node.IsEmpty) {
            return true;
        }

        if (node is OrConditionNode) {
            return false;
        }

        if (node is AndConditionNode and) {
            foreach (var child in and.Children) {
                if (!TryGetConjunctiveAtoms(child, out var childAtoms)) {
                    atoms = [];
                    return false;
                }

                atoms.AddRange(childAtoms);
            }

            return true;
        }

        atoms.Add(node);
        return true;
    }

    private static ConditionNode BuildConjunctiveCondition(IEnumerable<ConditionNode> atoms) {
        var list = atoms.Where(a => !a.IsEmpty).ToList();
        if (list.Count == 0) return EmptyConditionNode.Instance;
        if (list.Count == 1) return list[0];
        return new AndConditionNode(list);
    }

    /// <summary>
    /// Combines two conditions with AND.
    /// </summary>
    private static ConditionNode CombineConditions(ConditionNode a, ConditionNode b) {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return new AndConditionNode(a, b);
    }
}
