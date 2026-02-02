using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Enum serialization strategy.
/// Converts an enum to its underlying type and recursively handles it.
/// </summary>
public class EnumTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        return context.MemberTypeSym is INamedTypeSymbol { EnumUnderlyingType: not null } && !context.RoundState.IsEnumRound;
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberTypeSym = context.MemberTypeSym as INamedTypeSymbol;

        if (memberTypeSym?.EnumUnderlyingType == null) return;

        var roundState = context.RoundState.PushEnum((context.MemberTypeSym, memberTypeSym.EnumUnderlyingType));
        context.ExpandMembersCallback(seriBlock, deserBlock, context.ModelSym, [(m, context.ParentVar, roundState)]);
    }
}
