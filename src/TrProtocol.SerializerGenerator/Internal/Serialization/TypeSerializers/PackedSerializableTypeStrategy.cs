using Microsoft.CodeAnalysis;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Serialization strategy for types that implement IPackedSerializable.
/// Used for packed serialization of simple fixed-size types.
/// </summary>
public class PackedSerializableTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        return context.MemberTypeSym.AllInterfaces
            .Any(i => i.Name == nameof(IPackedSerializable));
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;
        var mTypeStr = context.TypeStr;

        seriBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess});");
        seriBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
        seriBlock.WriteLine();

        if (!memberTypeSym.IsAbstract) {
            deserBlock.WriteLine($"{memberAccess} = Unsafe.Read<{mTypeStr}>(ptr_current);");
            deserBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
            deserBlock.WriteLine();
        }
    }
}
