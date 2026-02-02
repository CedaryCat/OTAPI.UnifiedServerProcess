using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Extensions;

namespace TrProtocol.SerializerGenerator.Internal.Generation;

internal static class GenerationHelpers
{
    public static List<(string memberName, string memberType)> GetExternalMembers(INamedTypeSymbol typeSym) {
        var members = typeSym.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(t => t.Members.Select(m => new {
                Member = m,
                Location = m.GetLocation(),
            }))
            .OrderBy(m => m.Location.SourceTree?.FilePath ?? "")
            .ThenBy(m => m.Location.SourceSpan.Start)
            .Select(m => m.Member)
            .ToArray();

        var results = new List<(string memberName, string memberType)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in members) {
            var hasExternalMember = member.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a => a.AttributeMatch<ExternalMemberAttribute>());
            if (!hasExternalMember) {
                continue;
            }

            if (member is PropertyDeclarationSyntax prop) {
                var name = prop.Identifier.ToString();
                if (seen.Add(name)) {
                    results.Add((name, prop.Type.ToString()));
                }
            }
            else if (member is FieldDeclarationSyntax field) {
                var type = field.Declaration.Type.ToString();
                foreach (var variable in field.Declaration.Variables) {
                    var name = variable.Identifier.ToString();
                    if (seen.Add(name)) {
                        results.Add((name, type));
                    }
                }
            }
        }

        return results;
    }

    public static (string externalMemberParams, string externalMemberParamsCall) GetExternalMemberParams(IReadOnlyList<(string memberName, string memberType)> externalMembers) {
        if (externalMembers.Count == 0) {
            return ("", "");
        }

        var externalMemberParams = $", {string.Join(", ", externalMembers.Select(m => $"{m.memberType} _{m.memberName} = default"))}";
        var externalMemberParamsCall = $", {string.Join(", ", externalMembers.Select(m => $"_{m.memberName}"))}";
        return (externalMemberParams, externalMemberParamsCall);
    }

    public static bool HasWriteContent(INamedTypeSymbol typeSym) {
        return HasMethod(typeSym, "WriteContent", parameters => parameters.Length == 1
            && IsRefVoidPointer(parameters[0]));
    }

    public static bool HasReadContent(INamedTypeSymbol typeSym) {
        return HasMethod(typeSym, "ReadContent", parameters => parameters.Length == 1
            && IsRefVoidPointer(parameters[0]));
    }

    public static bool HasReadContentLengthAware(INamedTypeSymbol typeSym) {
        return HasMethod(typeSym, "ReadContent", parameters => parameters.Length == 2
            && IsRefVoidPointer(parameters[0])
            && IsVoidPointer(parameters[1]));
    }

    private static bool HasMethod(INamedTypeSymbol typeSym, string methodName, Func<ImmutableArray<IParameterSymbol>, bool> parameterPredicate) {
        foreach (var member in typeSym.GetMembers().OfType<IMethodSymbol>()) {
            if (member.IsStatic || !member.ReturnsVoid) {
                continue;
            }

            var matchesName = member.Name == methodName
                || member.ExplicitInterfaceImplementations.Any(i => i.Name == methodName);
            if (!matchesName) {
                continue;
            }

            if (parameterPredicate(member.Parameters)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsRefVoidPointer(IParameterSymbol param) {
        return param.RefKind == RefKind.Ref && IsVoidPointer(param);
    }

    private static bool IsVoidPointer(IParameterSymbol param) {
        if (param.Type is not IPointerTypeSymbol pointer) {
            return false;
        }
        return pointer.PointedAtType.SpecialType == SpecialType.System_Void;
    }
}

