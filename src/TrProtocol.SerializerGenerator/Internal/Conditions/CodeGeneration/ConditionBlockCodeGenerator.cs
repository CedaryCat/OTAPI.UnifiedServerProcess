using TrProtocol.SerializerGenerator.Internal.Conditions.Optimization;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;
using TrProtocol.SerializerGenerator.Internal.Conditions.Model;

namespace TrProtocol.SerializerGenerator.Internal.Conditions.CodeGeneration;

/// <summary>
/// Generates merged conditional blocks produced by <see cref="AdjacentFieldMerger"/>.
/// </summary>
public static class ConditionBlockCodeGenerator
{
    public static void GenerateConditionBlocks(
        IEnumerable<ConditionBlock> blocks,
        IReadOnlyDictionary<SerializationExpandContext, (IReadOnlyList<SourceNode> seriSources, IReadOnlyList<SourceNode> deserSources)> memberSources,
        BlockNode seriOut,
        BlockNode deserOut,
        string? parentVar) {
        foreach (var block in blocks) {
            GenerateConditionBlock(block, memberSources, seriOut, deserOut, parentVar);
        }
    }

    private static void GenerateConditionBlock(
        ConditionBlock block,
        IReadOnlyDictionary<SerializationExpandContext, (IReadOnlyList<SourceNode> seriSources, IReadOnlyList<SourceNode> deserSources)> memberSources,
        BlockNode seriOut,
        BlockNode deserOut,
        string? parentVar) {
        var seriBlock = new BlockNode(seriOut);
        var deserBlock = new BlockNode(deserOut);

        // 1) First, group adjacent members with identical AdditionalCondition to avoid repeated inner if blocks.
        var groups = new List<MemberGroup>();
        for (var i = 0; i < block.Members.Count;) {
            var first = block.Members[i];
            if (!memberSources.TryGetValue(first.Member, out var firstSources)) {
                i++;
                continue;
            }

            var additional = first.AdditionalCondition;
            var additionalKey = additional.IsEmpty ? null : additional.GetNormalizedKey();
            var groupParentVar = additional.IsEmpty ? null : first.ParentVar;

            var seriSources = new List<SourceNode>(firstSources.seriSources);
            var deserSources = new List<SourceNode>(firstSources.deserSources);
            i++;

            for (; i < block.Members.Count; i++) {
                var next = block.Members[i];
                if (!memberSources.TryGetValue(next.Member, out var nextSources)) {
                    continue;
                }

                var nextAdditional = next.AdditionalCondition;
                var nextKey = nextAdditional.IsEmpty ? null : nextAdditional.GetNormalizedKey();
                var nextParentVar = nextAdditional.IsEmpty ? null : next.ParentVar;

                if (additionalKey != nextKey || groupParentVar != nextParentVar) {
                    break;
                }

                seriSources.AddRange(nextSources.seriSources);
                deserSources.AddRange(nextSources.deserSources);
            }

            groups.Add(new MemberGroup(additional, groupParentVar, seriSources, deserSources));
        }

        // 2) Emit groups: support minimal "X / !X" adjacent groups => merge into if/else.
        for (var gi = 0; gi < groups.Count; gi++) {
            var group = groups[gi];

            if (group.Additional.IsEmpty) {
                seriBlock.Sources.AddRange(group.SeriSources);
                deserBlock.Sources.AddRange(group.DeserSources);
                continue;
            }

            if (gi + 1 < groups.Count) {
                var next = groups[gi + 1];
                if (!next.Additional.IsEmpty
                    && group.ParentVar == next.ParentVar
                    && AreNegations(group.Additional, next.Additional)) {
                    var seriExpr = group.Additional.ToConditionExpression(group.ParentVar, true);
                    var deserExpr = group.Additional.ToConditionExpression(group.ParentVar, false);

                    EmitIfElse(seriBlock, seriExpr, group.SeriSources, next.SeriSources);
                    EmitIfElse(deserBlock, deserExpr, group.DeserSources, next.DeserSources);

                    gi++; // consume next
                    continue;
                }
            }

            var seriGroup = new BlockNode(seriBlock);
            seriGroup.Sources.AddRange(group.SeriSources);
            var deserGroup = new BlockNode(deserBlock);
            deserGroup.Sources.AddRange(group.DeserSources);

            var seriSingleExpr = group.Additional.ToConditionExpression(group.ParentVar, true);
            var deserSingleExpr = group.Additional.ToConditionExpression(group.ParentVar, false);
            seriGroup.WarpBlock(($"if ({seriSingleExpr}) ", false));
            deserGroup.WarpBlock(($"if ({deserSingleExpr}) ", false));

            seriBlock.Sources.AddRange(seriGroup.Sources);
            deserBlock.Sources.AddRange(deserGroup.Sources);
        }

        foreach (var nested in block.NestedBlocks) {
            GenerateConditionBlock(nested, memberSources, seriBlock, deserBlock, parentVar);
        }

        if (!block.Condition.IsEmpty) {
            var seriExpr = block.Condition.ToConditionExpression(parentVar, true);
            var deserExpr = block.Condition.ToConditionExpression(parentVar, false);
            seriBlock.WarpBlock(($"if ({seriExpr}) ", false));
            deserBlock.WarpBlock(($"if ({deserExpr}) ", false));
        }

        seriOut.Sources.AddRange(seriBlock.Sources);
        deserOut.Sources.AddRange(deserBlock.Sources);
    }

    private sealed record MemberGroup(
        ConditionNode Additional,
        string? ParentVar,
        IReadOnlyList<SourceNode> SeriSources,
        IReadOnlyList<SourceNode> DeserSources);

    private static void EmitIfElse(BlockNode outBlock, string ifCondition, IReadOnlyList<SourceNode> trueSources, IReadOnlyList<SourceNode> falseSources) {
        outBlock.Sources.Add(new AppendTextNode($"if ({ifCondition}) ", outBlock));
        var trueBlock = new BlockNode(outBlock);
        trueBlock.Sources.AddRange(trueSources);
        outBlock.Sources.Add(trueBlock);

        outBlock.Sources.Add(new AppendTextNode("else ", outBlock));
        var falseBlock = new BlockNode(outBlock);
        falseBlock.Sources.AddRange(falseSources);
        outBlock.Sources.Add(falseBlock);
    }

    private static bool AreNegations(ConditionNode a, ConditionNode b) {
        if (a is AndConditionNode or OrConditionNode) return false;
        if (b is AndConditionNode or OrConditionNode) return false;

        return TryGetNegatableKey(a, out var keyA, out var expectedA)
            && TryGetNegatableKey(b, out var keyB, out var expectedB)
            && keyA == keyB
            && expectedA != expectedB;
    }

    private static bool TryGetNegatableKey(ConditionNode node, out string key, out bool expectedValue) {
        switch (node) {
            case BooleanConditionNode b:
                key = $"BOOL:{b.MemberName}";
                expectedValue = b.ExpectedValue;
                return true;
            case BitsByteConditionNode b:
                key = $"BITS:{b.MemberName}[{b.Index}]";
                expectedValue = b.ExpectedValue;
                return true;
            case LookupConditionNode l:
                key = $"LOOKUP:{l.TableName}[{l.KeyMemberName}]";
                expectedValue = l.ExpectedValue;
                return true;
            case ArrayIndexConditionNode a:
                key = $"ARRAY:{a.MemberName}[{a.IndexVariable}+{a.IndexOffset}]";
                expectedValue = a.ExpectedValue;
                return true;
            default:
                key = "";
                expectedValue = default;
                return false;
        }
    }
}
