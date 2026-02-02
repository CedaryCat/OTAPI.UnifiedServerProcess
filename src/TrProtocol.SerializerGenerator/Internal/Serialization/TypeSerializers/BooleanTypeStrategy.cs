using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Boolean serialization strategy.
/// Serializes bool as byte (true=1, false=0).
/// </summary>
public class BooleanTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        return context.TypeStr is "bool" or nameof(Boolean);
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var memberAccess = context.MemberAccess;

        seriBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess} ? (byte)1 : (byte)0);");
        seriBlock.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
        seriBlock.WriteLine();

        deserBlock.WriteLine($"{memberAccess} = Unsafe.Read<byte>(ptr_current) != 0;");
        deserBlock.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
        deserBlock.WriteLine();
    }
}
