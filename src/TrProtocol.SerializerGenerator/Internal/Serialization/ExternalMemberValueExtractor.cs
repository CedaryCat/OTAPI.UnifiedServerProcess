using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

/// <summary>
/// Extracts external member default values from ExternalMemberValueAttribute decorations.
/// </summary>
public static class ExternalMemberValueExtractor
{
    /// <summary>
    /// Extracts all external member default values from a serialization context.
    /// </summary>
    /// <param name="m">The serialization context for the member.</param>
    /// <param name="memberTypeSym">The type symbol of the member.</param>
    /// <returns>A list of tuples containing member names and their default values.</returns>
    /// <exception cref="DiagnosticException">Thrown when attribute arguments are invalid.</exception>
    public static List<(string memberName, string value)> Extract(SerializationExpandContext m, ITypeSymbol memberTypeSym) {
        List<(string memberName, string memberValue)> defInnerMemberValueAssigns = [];

        foreach (var defMemberValueAttr in m.Attributes.Where(a => a.AttributeMatch<ExternalMemberValueAttribute>())) {
            var attrParams = defMemberValueAttr.ExtractAttributeParams();
            string memberName;
            string value;

            // Parse member name from first argument
            if (attrParams[0].IsLiteralExpression(out var text1) && text1.StartsWith("\"") && text1.EndsWith("\"")) {
                memberName = text1[1..^1];
            }
            else if (attrParams[0] is InvocationExpressionSyntax invo1 && invo1.Expression.ToString() == "nameof") {
                memberName = invo1.ArgumentList.Arguments.First().Expression.ToString();
            }
            else {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "SCG16",
                            "condition attribute invaild",
                            "condition attribute argument of member '{0}' is invaild.",
                            "",
                            DiagnosticSeverity.Error,
                            true),
                        defMemberValueAttr.GetLocation(),
                        m.MemberName));
            }

            memberName = memberName.Split('.').Last();

            // Parse value from second argument
            value = ParseValueArgument(attrParams[1], defMemberValueAttr, m.MemberName);

            // Validate and add the member value assignment
            ValidateAndAddMemberValue(m, memberTypeSym, memberName, value, defInnerMemberValueAssigns);
        }

        return defInnerMemberValueAssigns;
    }

    private static string ParseValueArgument(ExpressionSyntax valueExpr, AttributeSyntax attr, string memberName) {
        if (valueExpr.IsLiteralExpression(out var text2)) {
            if (text2.StartsWith("\"") && text2.EndsWith("\"")) {
                return text2;
            }
            else if (bool.TryParse(text2, out var pred)) {
                return pred.ToString().ToLower();
            }
            else if (long.TryParse(text2, out var num)) {
                return num.ToString();
            }
            else if (double.TryParse(text2, out var num2)) {
                return num2.ToString();
            }
            else {
                ThrowInvalidAttributeException(attr, memberName);
            }
        }
        else if (valueExpr is InvocationExpressionSyntax invo2) {
            if (invo2.Expression.ToString() == "nameof") {
                return $"\"{invo2.ArgumentList.Arguments.First().Expression}\"";
            }
            else if (invo2.Expression.ToString() == "sizeof") {
                return invo2.ToString();
            }
            else {
                ThrowInvalidAttributeException(attr, memberName);
            }
        }
        else {
            ThrowInvalidAttributeException(attr, memberName);
        }

        return string.Empty; // Unreachable, but required for compiler
    }

    private static void ThrowInvalidAttributeException(AttributeSyntax attr, string memberName) {
        throw new DiagnosticException(
            Diagnostic.Create(
                new DiagnosticDescriptor(
                    "SCG16",
                    "condition attribute invaild",
                    "condition attribute argument of member '{0}' model is invaild.",
                    "",
                    DiagnosticSeverity.Error,
                    true),
                attr.GetLocation(),
                memberName));
    }

    private static void ValidateAndAddMemberValue(
        SerializationExpandContext m,
        ITypeSymbol memberTypeSym,
        string memberName,
        string value,
        List<(string, string)> assignments) {
        ITypeSymbol type = memberTypeSym;
        if (memberTypeSym is IArrayTypeSymbol arrTypeSym) {
            type = arrTypeSym.ElementType;
        }

        var innerDVField = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.Name == memberName);
        var innerDVProp = type.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(f => f.Name == memberName);

        if (innerDVField is null && innerDVProp is null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG34",
                        "unexcepted member DefSymbol",
                        "Cannot find the default member value '{0}' in the type {1} of this member '{2}'",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    m.MemberType.GetLocation(),
                    memberName,
                    type.Name,
                    m.MemberName));
        }

        if (innerDVField is not null) {
            ValidateExternalMemberAttribute(innerDVField.GetAttributes(), m.MemberType.GetLocation());
            assignments.Add((innerDVField.Name, value));
        }
        else if (innerDVProp is not null) {
            ValidateExternalMemberAttribute(innerDVProp.GetAttributes(), m.MemberType.GetLocation());
            assignments.Add((innerDVProp.Name, value));
        }
    }

    private static void ValidateExternalMemberAttribute(System.Collections.Immutable.ImmutableArray<AttributeData> attributes, Location location) {
        if (!attributes.Any(a => a.AttributeClass?.Name == nameof(ExternalMemberAttribute))) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG35",
                        "unexcepted member DefSymbol",
                        "Only members decorated with {0} can be set as default values externally with {1}",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    location,
                    nameof(ExternalMemberAttribute),
                    nameof(ExternalMemberValueAttribute)));
        }
    }
}
