using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// 7-bit encoded integer serialization strategy.
/// Handles int and int-backed enums decorated with [Int7BitEncoded].
/// </summary>
public class Int7BitEncodedStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        if (!Is7BitEncoded(context)) {
            return false;
        }

        if (context.Member.MemberType is ArrayTypeSyntax) {
            return false;
        }

        var m = context.Member;
        var mTypeStr = context.TypeStr;

        var isValidType = mTypeStr is "int" or nameof(Int32);
        var isValidEnumType = context.RoundState.IsEnumRound && context.RoundState.EnumType.underlyingType.GetPredifinedName() is "int";

        if (!isValidType && !isValidType) {
            throw new DiagnosticException(Diagnostic.Create(
                DiagnosticDescriptors.Int7BitEncodedMemberInvalidType,
                m.Attributes.First(a => a.AttributeMatch<Int7BitEncodedAttribute>()).GetLocation(),
                m.MemberName,
                mTypeStr
            ));
        }

        return true;
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;

        bool isIntEnum = memberTypeSym.TypeKind == TypeKind.Enum &&
            ((INamedTypeSymbol)memberTypeSym).EnumUnderlyingType?.SpecialType is SpecialType.System_Int32;

        // Serialization: write as 7-bit encoded int.
        if (context.RoundState.IsEnumRound) {
            seriBlock.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, (int){memberAccess});");
        }
        else if (isIntEnum) {
            seriBlock.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, (int){memberAccess});");
        }
        else {
            seriBlock.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, {memberAccess});");
        }
        seriBlock.WriteLine();

        // Deserialization: read as 7-bit encoded int.
        if (context.RoundState.IsEnumRound) {
            deserBlock.WriteLine($"{memberAccess} = ({context.RoundState.EnumType.enumType.Name})CommonCode.Read7BitEncodedInt(ref ptr_current);");
        }
        else if (isIntEnum) {
            deserBlock.WriteLine($"{memberAccess} = ({memberTypeSym.Name})CommonCode.Read7BitEncodedInt(ref ptr_current);");
        }
        else {
            deserBlock.WriteLine($"{memberAccess} = CommonCode.Read7BitEncodedInt(ref ptr_current);");
        }
        deserBlock.WriteLine();
    }

    /// <summary>
    /// Checks whether Int7BitEncodedAttribute is applied (including inherited/overridden members).
    /// </summary>
    public static bool Is7BitEncoded(TypeSerializerContext context) {
        var is7BitEncoded = context.FieldMemberSym?.GetAttributes()
            .Any(a => a.AttributeClass?.Name == nameof(Int7BitEncodedAttribute)) == true
            || context.PropMemberSym?.GetAttributes()
            .Any(a => a.AttributeClass?.Name == nameof(Int7BitEncodedAttribute)) == true;

        // If not present on the current member, check overridden base members.
        if (!is7BitEncoded && context.PropMemberSym?.IsOverride == true) {
            var overriddenProp = context.PropMemberSym.OverriddenProperty;
            while (overriddenProp != null && !is7BitEncoded) {
                is7BitEncoded = overriddenProp.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == nameof(Int7BitEncodedAttribute));
                overriddenProp = overriddenProp.OverriddenProperty;
            }
        }

        return is7BitEncoded;
    }
}
