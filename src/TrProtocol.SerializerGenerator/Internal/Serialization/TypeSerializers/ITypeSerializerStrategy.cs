using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Type serializer strategy interface.
/// Each strategy is responsible for generating serialization/deserialization code for a specific kind of type.
/// </summary>
public interface ITypeSerializerStrategy
{
    /// <summary>
    /// Determines whether to stop further strategy propagation after this one.
    /// </summary>
    bool StopPropagation { get; }
    /// <summary>
    /// Determines whether this strategy can handle the current context.
    /// </summary>
    /// <param name="context">The type serializer context.</param>
    /// <returns>true if this strategy can handle the type.</returns>
    bool CanHandle(TypeSerializerContext context);

    /// <summary>
    /// Generates serialization and deserialization code.
    /// </summary>
    /// <param name="context">The type serializer context.</param>
    /// <param name="seriBlock">The serialization code block.</param>
    /// <param name="deserBlock">The deserialization code block.</param>
    void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock);
}
