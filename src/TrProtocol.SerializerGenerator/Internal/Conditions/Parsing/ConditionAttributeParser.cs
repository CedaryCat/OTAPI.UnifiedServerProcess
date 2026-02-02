using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Terraria;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Conditions.Model;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Conditions.Parsing;

/// <summary>
/// Condition attribute parser that converts attribute syntax into condition nodes.
/// </summary>
public static class ConditionAttributeParser
{
    private static DiagnosticException CreateArgumentInvalid(AttributeSyntax attribute, SerializationExpandContext member, string modelTypeName) {
        return new DiagnosticException(
            Diagnostic.Create(
                DiagnosticDescriptors.ConditionAttributeArgumentInvalid,
                attribute.GetLocation(),
                member.MemberName,
                modelTypeName));
    }

    private static DiagnosticException CreateComparisonArgumentInvalid(AttributeSyntax attribute, SerializationExpandContext member, string modelTypeName) {
        return new DiagnosticException(
            Diagnostic.Create(
                DiagnosticDescriptors.ConditionComparisonArgumentInvalid,
                attribute.GetLocation(),
                member.MemberName,
                modelTypeName));
    }

    private static DiagnosticException CreateArrayArgumentInvalid(AttributeSyntax attribute, SerializationExpandContext member, string modelTypeName) {
        return new DiagnosticException(
            Diagnostic.Create(
                DiagnosticDescriptors.ArrayConditionArgumentInvalid,
                attribute.GetLocation(),
                member.MemberName,
                modelTypeName));
    }

    /// <summary>
    /// Parses a single condition attribute and returns the corresponding condition node.
    /// </summary>
    /// <param name="attribute">The attribute syntax.</param>
    /// <param name="modelSym">The model type symbol.</param>
    /// <param name="indexVariable">The array index variable name (used by ConditionArray).</param>
    /// <returns>The parsed condition node, or null if the attribute is not a condition attribute.</returns>
    public static ConditionNode? ParseAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        INamedTypeSymbol modelSym,
        string modelTypeName,
        string? indexVariable = null) {
        if (attribute.ArgumentList is null)
            return null;

        // [Condition(...)]
        if (attribute.AttributeMatch<ConditionAttribute>()) {
            return ParseConditionAttribute(attribute, member, modelSym, modelTypeName);
        }

        // [ConditionLookupMatch/NotMatch(...)]
        if (attribute.AttributeMatch<ConditionLookupMatchAttribute>()) {
            return ParseLookupMatchAttribute(attribute, member, modelTypeName, expectedMatch: true);
        }
        if (attribute.AttributeMatch<ConditionLookupNotMatchAttribute>()) {
            return ParseLookupMatchAttribute(attribute, member, modelTypeName, expectedMatch: false);
        }

        // [ConditionEqual/NotEqual/GreaterThan/...]
        var comparisonResult = TryParseComparisonAttribute(attribute, member, modelSym, modelTypeName);
        if (comparisonResult != null) {
            return comparisonResult;
        }

        // [ConditionLookupEqual/NotEqual]
        var lookupComparisonResult = TryParseLookupComparisonAttribute(attribute, member, modelTypeName);
        if (lookupComparisonResult != null) {
            return lookupComparisonResult;
        }

        // [ConditionArray(...)]
        if (attribute.AttributeMatch<ConditionArrayAttribute>() && indexVariable != null) {
            return ParseConditionArrayAttribute(attribute, member, modelTypeName, indexVariable);
        }

        return null;
    }

    private static ConditionNode ParseConditionAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        INamedTypeSymbol modelSym,
        string modelTypeName) {
        try {
            var args = attribute.ArgumentList!.Arguments;

            string memberName = ExtractMemberName(args[0].Expression) ?? throw CreateArgumentInvalid(attribute, member, modelTypeName);

            string? conditionIndex = null;
            bool conditionPred = true;

            if (args.Count == 3) {
                conditionIndex = ExtractIndexOrSizeof(args[1].Expression);
                if (conditionIndex == null) throw CreateArgumentInvalid(attribute, member, modelTypeName);

                var predVal = ExtractBoolLiteral(args[2].Expression);
                if (!predVal.HasValue) throw CreateArgumentInvalid(attribute, member, modelTypeName);

                conditionPred = predVal.Value;
            }
            else if (args.Count == 2) {
                var arg1 = args[1].Expression;
                var boolVal = ExtractBoolLiteral(arg1);
                if (boolVal.HasValue) {
                    conditionPred = boolVal.Value;
                }
                else {
                    conditionIndex = ExtractIndexOrSizeof(arg1);
                    if (conditionIndex == null) throw CreateArgumentInvalid(attribute, member, modelTypeName);
                }
            }
            else if (args.Count == 1) {
                conditionPred = true;
            }
            else {
                throw CreateArgumentInvalid(attribute, member, modelTypeName);
            }

            var conditionMember = modelSym.GetMembers(memberName);
            var conditionTypes = conditionMember.OfType<IFieldSymbol>().Select(f => f.Type)
                .Concat(conditionMember.OfType<IPropertySymbol>().Select(p => p.Type));

            if (conditionIndex is not null && conditionTypes.Any(t => t.Name != nameof(BitsByte))) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ConditionMemberMustBeBitsByte,
                        attribute.GetLocation(),
                        nameof(BitsByte)));
            }

            if (conditionIndex is null && conditionTypes.Any(t => t.Name != nameof(Boolean))) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ConditionMemberMustBeBoolean,
                        attribute.GetLocation(),
                        nameof(Boolean)));
            }

            return conditionIndex is not null
                ? new BitsByteConditionNode(memberName, conditionIndex, conditionPred)
                : new BooleanConditionNode(memberName, conditionPred);
        }
        catch (DiagnosticException) {
            throw;
        }
        catch {
            throw CreateArgumentInvalid(attribute, member, modelTypeName);
        }
    }

    private static ConditionNode ParseLookupMatchAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        string modelTypeName,
        bool expectedMatch) {
        try {
            var args = attribute.ArgumentList!.Arguments;

            string tableName = ExtractMemberName(args[0].Expression) ?? throw CreateArgumentInvalid(attribute, member, modelTypeName);
            string keyMemberName = ExtractMemberName(args[1].Expression) ?? throw CreateArgumentInvalid(attribute, member, modelTypeName);

            bool pred = true;
            if (args.Count >= 3) {
                var predVal = ExtractBoolLiteral(args[2].Expression);
                if (!predVal.HasValue) throw CreateArgumentInvalid(attribute, member, modelTypeName);
                pred = predVal.Value;
            }

            var expectedValue = expectedMatch ? pred : !pred;
            return new LookupConditionNode(tableName, keyMemberName, expectedValue);
        }
        catch (DiagnosticException) {
            throw;
        }
        catch {
            throw CreateArgumentInvalid(attribute, member, modelTypeName);
        }
    }

    private static ConditionNode? TryParseComparisonAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        INamedTypeSymbol modelSym,
        string modelTypeName) {
        var (op, _) = attribute.Name.ToString() switch {
            nameof(ConditionEqualAttribute) or "ConditionEqual" => ("==", 0),
            nameof(ConditionNotEqualAttribute) or "ConditionNotEqual" => ("!=", 0),
            nameof(ConditionGreaterThanAttribute) or "ConditionGreaterThan" => (">", 0),
            nameof(ConditionGreaterThanEqualAttribute) or "ConditionGreaterThanEqual" => (">=", 0),
            nameof(ConditionLessThanAttribute) or "ConditionLessThan" => ("<", 0),
            nameof(ConditionLessThanEqualAttribute) or "ConditionLessThanEqual" => ("<=", 0),
            _ => (null, -1)
        };

        if (op == null) return null;

        try {
            var args = attribute.ArgumentList!.Arguments;

            string memberName = ExtractMemberName(args[0].Expression) ?? throw CreateComparisonArgumentInvalid(attribute, member, modelTypeName);
            string rightValue = ExtractComparisonRightValue(args[1].Expression, out bool needsCast);

            string? castType = null;
            if (needsCast) {
                var memberSymbols = modelSym.GetMembers(memberName);
                var type = memberSymbols.OfType<IFieldSymbol>().Select(f => f.Type)
                    .Concat(memberSymbols.OfType<IPropertySymbol>().Select(p => p.Type))
                    .FirstOrDefault();

                if (type is null) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            DiagnosticDescriptors.ConditionComparisonMemberNotFound,
                            attribute.GetLocation()));
                }

                if (type.AllInterfaces.Any(i => i.Name == nameof(IConvertible))) {
                    castType = type.ToDisplayString();
                }
            }

            return new ComparisonConditionNode(memberName, op, rightValue, castType);
        }
        catch (DiagnosticException) {
            throw;
        }
        catch {
            throw CreateComparisonArgumentInvalid(attribute, member, modelTypeName);
        }
    }

    private static ConditionNode? TryParseLookupComparisonAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        string modelTypeName) {
        var (op, _) = attribute.Name.ToString() switch {
            nameof(ConditionLookupEqualAttribute) or "ConditionLookupEqual" => ("==", 1),
            nameof(ConditionLookupNotEqualAttribute) or "ConditionLookupNotEqual" => ("!=", 1),
            _ => (null, -1)
        };

        if (op == null) return null;

        try {
            var args = attribute.ArgumentList!.Arguments;

            string tableName = ExtractMemberName(args[0].Expression) ?? throw CreateComparisonArgumentInvalid(attribute, member, modelTypeName);
            string keyMemberName = ExtractMemberName(args[1].Expression) ?? throw CreateComparisonArgumentInvalid(attribute, member, modelTypeName);
            string rightValue = ExtractComparisonRightValue(args[2].Expression, out _);

            return new LookupComparisonConditionNode(tableName, keyMemberName, op, rightValue);
        }
        catch (DiagnosticException) {
            throw;
        }
        catch {
            throw CreateComparisonArgumentInvalid(attribute, member, modelTypeName);
        }
    }

    private static ConditionNode ParseConditionArrayAttribute(
        AttributeSyntax attribute,
        SerializationExpandContext member,
        string modelTypeName,
        string indexVariable) {
        try {
            var args = attribute.ArgumentList!.Arguments;

            string memberName = ExtractMemberName(args[0].Expression) ?? throw CreateArrayArgumentInvalid(attribute, member, modelTypeName);
            string indexOffset = ExtractIndexOrSizeof(args[1].Expression) ?? throw CreateArrayArgumentInvalid(attribute, member, modelTypeName);

            bool pred = true;
            if (args.Count >= 3) {
                var predVal = ExtractBoolLiteral(args[2].Expression);
                if (!predVal.HasValue) throw CreateArrayArgumentInvalid(attribute, member, modelTypeName);
                pred = predVal.Value;
            }

            return new ArrayIndexConditionNode(memberName, indexOffset, indexVariable, pred);
        }
        catch (DiagnosticException) {
            throw;
        }
        catch {
            throw CreateArrayArgumentInvalid(attribute, member, modelTypeName);
        }
    }

    private static ConditionNode ParseConditionAttribute(AttributeSyntax attribute, INamedTypeSymbol modelSym) {
        var args = attribute.ArgumentList!.Arguments;

        // Parse member name.
        string memberName = ExtractMemberName(args[0].Expression)
            ?? throw CreateParseException(attribute, "Invalid member name");

        string? conditionIndex = null;
        bool conditionPred = true;

        if (args.Count == 3) {
            // [Condition(nameof(M), index, pred)]
            conditionIndex = ExtractIndexOrSizeof(args[1].Expression);
            conditionPred = ExtractBoolLiteral(args[2].Expression) ?? true;
        }
        else if (args.Count == 2) {
            // [Condition(nameof(M), indexOrPred)]
            var arg1 = args[1].Expression;
            var boolVal = ExtractBoolLiteral(arg1);
            if (boolVal.HasValue) {
                conditionPred = boolVal.Value;
            }
            else {
                conditionIndex = ExtractIndexOrSizeof(arg1);
            }
        }

        // Determine whether this is a BitsByte or a Boolean condition.
        if (conditionIndex != null) {
            return new BitsByteConditionNode(memberName, conditionIndex, conditionPred);
        }
        else {
            return new BooleanConditionNode(memberName, conditionPred);
        }
    }

    private static ConditionNode ParseLookupMatchAttribute(AttributeSyntax attribute, bool expectedValue) {
        var args = attribute.ArgumentList!.Arguments;

        string tableName = ExtractMemberName(args[0].Expression)
            ?? throw CreateParseException(attribute, "Invalid table name");
        string keyMemberName = ExtractMemberName(args[1].Expression)
            ?? throw CreateParseException(attribute, "Invalid key member name");

        bool pred = args.Count >= 3
            ? ExtractBoolLiteral(args[2].Expression) ?? true
            : true;

        return new LookupConditionNode(tableName, keyMemberName, expectedValue == pred);
    }

    private static ConditionNode? TryParseComparisonAttribute(AttributeSyntax attribute, INamedTypeSymbol modelSym) {
        var (op, mode) = attribute.Name.ToString() switch {
            nameof(ConditionEqualAttribute) or "ConditionEqual" => ("==", 0),
            nameof(ConditionNotEqualAttribute) or "ConditionNotEqual" => ("!=", 0),
            nameof(ConditionGreaterThanAttribute) or "ConditionGreaterThan" => (">", 0),
            nameof(ConditionGreaterThanEqualAttribute) or "ConditionGreaterThanEqual" => (">=", 0),
            nameof(ConditionLessThanAttribute) or "ConditionLessThan" => ("<", 0),
            nameof(ConditionLessThanEqualAttribute) or "ConditionLessThanEqual" => ("<=", 0),
            _ => (null, -1)
        };

        if (op == null) return null;

        var args = attribute.ArgumentList!.Arguments;

        string memberName = ExtractMemberName(args[0].Expression)
            ?? throw CreateParseException(attribute, "Invalid member name");

        string rightValue = ExtractComparisonRightValue(args[1].Expression, out bool needsCast);

        string? castType = null;
        if (needsCast) {
            var member = modelSym.GetMembers(memberName);
            var type = member.OfType<IFieldSymbol>().Select(f => f.Type)
                .Concat(member.OfType<IPropertySymbol>().Select(p => p.Type))
                .FirstOrDefault();
            if (type?.AllInterfaces.Any(i => i.Name == nameof(IConvertible)) == true) {
                castType = type.ToDisplayString();
            }
        }

        return new ComparisonConditionNode(memberName, op, rightValue, castType);
    }

    private static ConditionNode? TryParseLookupComparisonAttribute(AttributeSyntax attribute) {
        var (op, mode) = attribute.Name.ToString() switch {
            nameof(ConditionLookupEqualAttribute) or "ConditionLookupEqual" => ("==", 1),
            nameof(ConditionLookupNotEqualAttribute) or "ConditionLookupNotEqual" => ("!=", 1),
            _ => (null, -1)
        };

        if (op == null) return null;

        var args = attribute.ArgumentList!.Arguments;

        string tableName = ExtractMemberName(args[0].Expression)
            ?? throw CreateParseException(attribute, "Invalid table name");
        string keyMemberName = ExtractMemberName(args[1].Expression)
            ?? throw CreateParseException(attribute, "Invalid key member name");
        string rightValue = ExtractComparisonRightValue(args[2].Expression, out _);

        return new LookupComparisonConditionNode(tableName, keyMemberName, op, rightValue);
    }

    private static ConditionNode ParseConditionArrayAttribute(AttributeSyntax attribute, string indexVariable) {
        var args = attribute.ArgumentList!.Arguments;

        string memberName = ExtractMemberName(args[0].Expression)
            ?? throw CreateParseException(attribute, "Invalid member name");
        string indexOffset = ExtractIndexOrSizeof(args[1].Expression)
            ?? throw CreateParseException(attribute, "Invalid index offset");
        bool pred = args.Count >= 3
            ? ExtractBoolLiteral(args[2].Expression) ?? true
            : true;

        return new ArrayIndexConditionNode(memberName, indexOffset, indexVariable, pred);
    }

    #region Helper Methods

    private static string? ExtractMemberName(ExpressionSyntax expr) {
        if (expr.IsLiteralExpression(out var text) && text.StartsWith("\"") && text.EndsWith("\"")) {
            return text[1..^1];
        }
        if (expr is InvocationExpressionSyntax invo && invo.Expression.ToString() == "nameof") {
            return invo.ArgumentList.Arguments.First().Expression.ToString();
        }
        return null;
    }

    private static string? ExtractIndexOrSizeof(ExpressionSyntax expr) {
        if (expr.IsLiteralExpression(out var text) && byte.TryParse(text, out _)) {
            return text;
        }
        if (expr is InvocationExpressionSyntax invo && invo.Expression.ToString() == "sizeof") {
            return invo.ToString();
        }
        return null;
    }

    private static bool? ExtractBoolLiteral(ExpressionSyntax expr) {
        if (expr.IsLiteralExpression(out var text) && bool.TryParse(text, out var result)) {
            return result;
        }
        return null;
    }

    private static string ExtractComparisonRightValue(ExpressionSyntax expr, out bool needsCast) {
        needsCast = true;

        // Integer literal
        if (expr.IsLiteralExpression(out var text) && int.TryParse(text, out _)) {
            return text;
        }

        // Negative integer literal
        if (expr is PrefixUnaryExpressionSyntax pu &&
            pu.OperatorToken.Text == "-" &&
            pu.Operand.IsLiteralExpression(out var oppositeText) &&
            int.TryParse(oppositeText, out _)) {
            return pu.ToString();
        }

        // sizeof(...)
        if (expr is InvocationExpressionSyntax invo && invo.Expression.ToString() == "sizeof") {
            return invo.ToString();
        }

        // Enum value
        if (expr is MemberAccessExpressionSyntax enumAccess) {
            needsCast = false;
            return enumAccess.ToString();
        }

        throw new InvalidOperationException($"Unsupported comparison value: {expr}");
    }

    private static InvalidOperationException CreateParseException(AttributeSyntax attribute, string message) {
        return new InvalidOperationException($"Error parsing {attribute.Name}: {message}");
    }

    #endregion
}
