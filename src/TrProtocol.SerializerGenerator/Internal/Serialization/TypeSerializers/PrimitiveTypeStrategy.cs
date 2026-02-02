using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Primitive numeric serialization strategy.
/// Handles: byte, sbyte, short, ushort, int, uint, long, ulong, float, double, decimal
/// </summary>
public class PrimitiveTypeStrategy : ITypeSerializerStrategy
{
    private static readonly HashSet<string> PrimitiveTypes =
    [
        "byte", nameof(Byte),
        "sbyte", nameof(SByte),
        "ushort", nameof(UInt16),
        "short", nameof(Int16),
        "uint", nameof(UInt32),
        "int", nameof(Int32),
        "ulong", nameof(UInt64),
        "long", nameof(Int64),
        "float", nameof(Single),
        "double", nameof(Double),
        "decimal", nameof(Decimal)
    ];
    public bool StopPropagation => true;

    public bool CanHandle(TypeSerializerContext context) {
        return PrimitiveTypes.Contains(context.TypeStr);
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var mTypeStr = context.TypeStr;

        // Check SerializeAsAttribute.
        var serializeAsType = GetSerializeAsType(context);

        if (serializeAsType is not null) {
            var typeStr = serializeAsType.GetPredifinedName();
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({typeStr}){memberAccess});");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{typeStr}>(ptr_current, 1);");
            seriBlock.WriteLine();

            deserBlock.WriteLine($"{memberAccess} = ({mTypeStr})Unsafe.Read<{typeStr}>(ptr_current);");
            deserBlock.WriteLine($"ptr_current = Unsafe.Add<{typeStr}>(ptr_current, 1);");
            deserBlock.WriteLine();
        }
        else {
            var castPrefix = context.RoundState.IsEnumRound ? $"({mTypeStr})" : "";
            var castSuffix = context.RoundState.IsEnumRound ? $"({context.RoundState.EnumType.enumType.Name})" : "";

            seriBlock.WriteLine($"Unsafe.Write(ptr_current, {castPrefix}{memberAccess});");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
            seriBlock.WriteLine();

            deserBlock.WriteLine($"{memberAccess} = {castSuffix}Unsafe.Read<{mTypeStr}>(ptr_current);");
            deserBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
            deserBlock.WriteLine();
        }
    }

    private static INamedTypeSymbol? GetSerializeAsType(TypeSerializerContext context) {
        var typeAsAttr = context.FieldMemberSym?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "SerializeAsAttribute");
        typeAsAttr ??= context.PropMemberSym?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "SerializeAsAttribute");

        if (typeAsAttr?.ConstructorArguments.Length > 0) {
            return typeAsAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
        }
        return null;
    }
}
