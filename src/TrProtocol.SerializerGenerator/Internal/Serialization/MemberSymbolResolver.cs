using Microsoft.CodeAnalysis;
using TrProtocol.Exceptions;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

/// <summary>
/// Resolves member symbols from type declarations and validates their existence.
/// </summary>
public static class MemberSymbolResolver
{
    /// <summary>
    /// Resolves the type symbol for a member and retrieves the corresponding field or property symbol.
    /// </summary>
    /// <param name="typeSym">The type symbol containing the member.</param>
    /// <param name="m">The serialization context for the member.</param>
    /// <param name="mTypeSym">Output: The resolved type symbol for the member.</param>
    /// <param name="fieldMemberSym">Output: The field symbol if the member is a field, otherwise null.</param>
    /// <param name="propMemberSym">Output: The property symbol if the member is a property, otherwise null.</param>
    /// <exception cref="DiagnosticException">Thrown when the member cannot be found or has an invalid type.</exception>
    public static void ResolveMemberSymbol(
        INamedTypeSymbol typeSym,
        SerializationExpandContext m,
        out ITypeSymbol mTypeSym,
        out IFieldSymbol? fieldMemberSym,
        out IPropertySymbol? propMemberSym) {
        var fieldsSym = typeSym.GetMembers().OfType<IFieldSymbol>().Where(f => f.DeclaredAccessibility is Accessibility.Public).ToArray();
        var propertiesSym = typeSym.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility is Accessibility.Public).ToArray();

        propMemberSym = propertiesSym.FirstOrDefault(p => p.Name == m.MemberName);
        fieldMemberSym = fieldsSym.FirstOrDefault(f => f.Name == m.MemberName);

        if (fieldMemberSym is not null && !m.IsProperty) {
            mTypeSym = fieldMemberSym.Type;
        }
        else if (propMemberSym is not null && m.IsProperty) {
            mTypeSym = propMemberSym.Type;
        }
        else {
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG11",
                        "unexcepted member DefSymbol missing",
                        "The member '{0}' of type '{1}' cannot be found in compilation",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    typeSym.Name));
        }

        if (mTypeSym.Name is nameof(Nullable<byte>)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG12",
                        "invaild member DefSymbol",
                        "Members '{0}' of type '{0}' cannot be null-assignable value types '{2}'",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    m.MemberType.GetLocation(),
                    m.MemberName,
                    typeSym.Name,
                    m.MemberType));
        }
    }
}
