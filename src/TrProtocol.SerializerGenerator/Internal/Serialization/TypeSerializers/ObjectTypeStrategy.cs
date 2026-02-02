using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Object strategy (skip serialization).
/// </summary>
public class ObjectTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;

    public bool CanHandle(TypeSerializerContext context) {
        return context.TypeStr is "object" or nameof(Object);
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        // object does not generate any serialization code.
    }
}
