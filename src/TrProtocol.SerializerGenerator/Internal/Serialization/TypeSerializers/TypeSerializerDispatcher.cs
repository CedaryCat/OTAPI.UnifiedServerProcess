using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Type serializer strategy dispatcher.
/// Selects an appropriate strategy based on the type and generates serialization code.
/// </summary>
public class TypeSerializerDispatcher
{
    private readonly IReadOnlyList<ITypeSerializerStrategy> _strategies;

    public TypeSerializerDispatcher(
        Dictionary<string, object> abstractTypesSymbols,
        CompilationContext compilationContext) {
        // Strategies are ordered by priority: more specific strategies should come first.
        _strategies = [
            new ObjectTypeStrategy(),                       // object: skip generation
            new Int7BitEncodedStrategy(),                   // [Int7BitEncoded] must come before primitive handling
            new PrimitiveTypeStrategy(),                    // byte, int, float, etc.
            new BooleanTypeStrategy(),                      // bool
            new StringTypeStrategy(),                       // string
            new ArrayTypeStrategy(compilationContext),      // T[]
            new PackedSerializableTypeStrategy(),           // IPackedSerializable
            new AbstractTypeStrategy(abstractTypesSymbols), // abstract types
            new BinarySerializableTypeStrategy(),           // IBinarySerializable
            new SerializableViewTypeStrategy(),             // ISerializableView
            
            new EnumTypeStrategy(),                         // enum (recursively handles the underlying type)
            new InlineTypeStrategy(compilationContext),     // inline expansion of type members
        ];
    }

    /// <summary>
    /// Static factory method that creates a dispatcher with the default strategy list.
    /// </summary>
    public static TypeSerializerDispatcher Create(
        Dictionary<string, object> abstractTypesSymbols,
        CompilationContext compilationContext) {
        return new TypeSerializerDispatcher(abstractTypesSymbols, compilationContext);
    }

    /// <summary>
    /// Generates serialization/deserialization code for a member.
    /// </summary>
    /// <param name="context">The type serializer context.</param>
    /// <param name="seriBlock">The serialization code block.</param>
    /// <param name="deserBlock">The deserialization code block.</param>
    public void Serialize(
        TypeSerializerContext context,
        BlockNode seriBlock,
        BlockNode deserBlock) {
        foreach (var strategy in _strategies) {
            if (strategy.CanHandle(context)) {
                strategy.GenerateSerialization(context, seriBlock, deserBlock);
                if (strategy.StopPropagation) {
                    return;
                }
            }
        }

        // No strategy matched: emit a diagnostic exception.
        throw new DiagnosticException(
            Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedMemberType,
                context.Member.MemberType.GetLocation(),
                context.Member.MemberName,
                context.ModelSym.Name,
                context.MemberTypeSym.Name));
    }

    /// <summary>
    /// Attempts to find a strategy that can handle this type.
    /// </summary>
    public bool TryFindStrategy(TypeSerializerContext context, out ITypeSerializerStrategy? strategy) {
        foreach (var s in _strategies) {
            if (s.CanHandle(context)) {
                strategy = s;
                return true;
            }
        }
        strategy = null;
        return false;
    }
}
