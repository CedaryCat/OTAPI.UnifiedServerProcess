using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Abstract-type serialization strategy.
/// Handles abstract types that provide a static Read{T} method.
/// </summary>
public class AbstractTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => false;

    private readonly Dictionary<string, object> _abstractTypesSymbols;

    public AbstractTypeStrategy(Dictionary<string, object> abstractTypesSymbols) {
        _abstractTypesSymbols = abstractTypesSymbols;
    }

    public bool CanHandle(TypeSerializerContext context) {
        if (!context.MemberTypeSym.IsAbstract)
            return false;

        var mTypefullName = context.MemberTypeSym.GetFullName();
        return _abstractTypesSymbols.ContainsKey(mTypefullName);
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;
        var externalMemberValueArgs = context.ExternalMemberValueArgs;

        // Track nullable members.
        if (context.ParentVar is null && context.IsConditional && !context.RoundState.IsArrayRound && !context.RoundState.IsEnumRound) {
            context.MemberNullables.Add(m.MemberName);
        }

        deserBlock.WriteLine($"{memberAccess} = {memberTypeSym.Name}.Read{memberTypeSym.Name}(ref ptr_current{externalMemberValueArgs});");
    }
}
