using Microsoft.CodeAnalysis;
using System.Diagnostics.CodeAnalysis;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Extensions;

public static class TypeSymbolExtensions
{
    public static IEnumerable<INamedTypeSymbol> GetAllBaseClasses(this INamedTypeSymbol type) {
        var current = type.BaseType;
        while (current != null) {
            yield return current;
            current = current.BaseType;
        }
    }
    public static IEnumerable<INamedTypeSymbol> GetAllInterfaces(this INamedTypeSymbol type) {
        return type.AllInterfaces;
    }
    public static IEnumerable<INamedTypeSymbol> GetFullInheritanceTree(this INamedTypeSymbol type) {
        var baseClasses = type.GetAllBaseClasses();
        var interfaces = type.GetAllInterfaces();
        return baseClasses
            .Concat(interfaces)
            .Distinct(SymbolEqualityComparer.Default)
            .OfType<INamedTypeSymbol>();
    }
    public static bool HasAbstractModelAttribute(this INamedTypeSymbol type, [NotNullWhen(true)] out PolymorphicImplsInfo? info) {
        var att = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == nameof(PolymorphicBaseAttribute));
        if (att is not null) {
            var discriminatorPropertyName = (string)(att.ConstructorArguments[1].Value ?? throw new NullReferenceException());

            // Check whether the discriminator property/field has Int7BitEncodedAttribute.
            var discriminatorMember = type.GetMembers(discriminatorPropertyName).FirstOrDefault();
            var is7BitEncoded = discriminatorMember?.GetAttributes()
                .Any(a => a.AttributeClass?.Name == nameof(Int7BitEncodedAttribute)) == true;

            info = new PolymorphicImplsInfo(
                type,
                ((INamedTypeSymbol)att.ConstructorArguments[0].Value!),
                discriminatorPropertyName,
                is7BitEncoded);
            return true;
        }
        info = null;
        return false;
    }
    public static bool HasAbstractModelAttribute(this INamedTypeSymbol type) {
        return type.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(PolymorphicBaseAttribute));
    }
    public static string GetPredifinedName(this ITypeSymbol type) {
        return type.Name switch {
            nameof(Boolean) => "bool",
            nameof(Byte) => "byte",
            nameof(SByte) => "sbyte",
            nameof(Int32) => "int",
            nameof(UInt32) => "uint",
            nameof(Int16) => "short",
            nameof(UInt16) => "ushort",
            nameof(Int64) => "long",
            nameof(UInt64) => "ulong",
            nameof(Single) => "float",
            nameof(Double) => "double",
            nameof(Decimal) => "decimal",
            nameof(String) => "string",
            nameof(Char) => "char",
            nameof(Object) => "object",
            "Void" => "void",
            _ => type.Name,
        };
    }
    public static string GetPredifinedName(this Type type) {
        return type.Name switch {
            nameof(Boolean) => "bool",
            nameof(Byte) => "byte",
            nameof(SByte) => "sbyte",
            nameof(Int32) => "int",
            nameof(UInt32) => "uint",
            nameof(Int16) => "short",
            nameof(UInt16) => "ushort",
            nameof(Int64) => "long",
            nameof(UInt64) => "ulong",
            nameof(Single) => "float",
            nameof(Double) => "double",
            nameof(Decimal) => "decimal",
            nameof(String) => "string",
            nameof(Char) => "char",
            nameof(Object) => "object",
            "Void" => "void",
            _ => type.Name,
        };
    }
    public static bool IsNumber(this ITypeSymbol type, bool includeEnum = false) {
        if (includeEnum && type.TypeKind == TypeKind.Enum) {
            return true;
        }
        return type.Name switch {
            nameof(Byte) => true,
            nameof(SByte) => true,
            nameof(Int32) => true,
            nameof(UInt32) => true,
            nameof(Int16) => true,
            nameof(UInt16) => true,
            nameof(Int64) => true,
            nameof(UInt64) => true,
            nameof(Single) => true,
            nameof(Double) => true,
            nameof(Decimal) => true,
            nameof(Char) => true,
            _ => false,
        };
    }
    public static string GetFullTypeName(this ITypeSymbol type) {
        var name = type.Name;
        var parent = type.ContainingSymbol;
        while (parent is ITypeSymbol t && !string.IsNullOrEmpty(t.Name)) {
            name = $"{t.Name}.{name}";
            parent = t.ContainingSymbol;
        }
        return name;
    }
    public static string GetFullNamespace(this ITypeSymbol type) {
        if (type.ContainingNamespace is not null) {
            var name = type.ContainingNamespace.Name;
            var parent = type.ContainingNamespace.ContainingNamespace;
            while (parent is INamespaceSymbol n && !string.IsNullOrEmpty(n.Name)) {
                name = $"{n.Name}.{name}";
                parent = n.ContainingNamespace;
            }
            return name;
        }
        return "";
    }
    public static string GetFullName(this ITypeSymbol type) {
        var name = type.Name;
        var parent = type.ContainingSymbol;
        while (parent is INamespaceOrTypeSymbol t && !string.IsNullOrEmpty(t.Name)) {
            name = $"{t.Name}.{name}";
            parent = t.ContainingSymbol;
        }
        return name;
    }
    public static bool InheritFrom(this ITypeSymbol type, string parentName, bool includeInterfaces = true) {
        var parent = type.BaseType;
        while (parent is not null) {
            if (parent.Name == parentName) {
                return true;
            }
            parent = parent.BaseType;
        }
        if (includeInterfaces) {
            foreach (var interf in type.AllInterfaces) {
                if (interf.Name == parentName) {
                    return true;
                }
            }
        }
        return false;
    }
    public static bool IsOrInheritFrom(this ITypeSymbol type, string parentName, bool includeInterfaces = true) => type.Name == parentName || type.InheritFrom(parentName, includeInterfaces);
}
