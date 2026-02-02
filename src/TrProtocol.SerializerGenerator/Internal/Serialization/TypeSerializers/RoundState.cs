using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Immutable per-member expansion state (array index rounds, enum underlying rounds).
/// Replaces the mutable stack state previously stored on <see cref="Internal.Models.SerializationExpandContext"/>.
/// </summary>
public readonly struct RoundState
{
    public static RoundState Empty { get; } = new(ImmutableStack<string[]>.Empty, ImmutableStack<(ITypeSymbol enumType, ITypeSymbol underlyingType)>.Empty);

    private readonly ImmutableStack<string[]> _arrayIndexNames;
    private readonly ImmutableStack<(ITypeSymbol enumType, ITypeSymbol underlyingType)> _enumRounds;

    private RoundState(
        ImmutableStack<string[]> arrayIndexNames,
        ImmutableStack<(ITypeSymbol enumType, ITypeSymbol underlyingType)> enumRounds) {
        _arrayIndexNames = arrayIndexNames;
        _enumRounds = enumRounds;
    }

    public bool IsArrayRound => !_arrayIndexNames.IsEmpty;
    public string[] IndexNames => _arrayIndexNames.Peek();

    public bool IsEnumRound => !_enumRounds.IsEmpty;
    public (ITypeSymbol enumType, ITypeSymbol underlyingType) EnumType => _enumRounds.Peek();

    public RoundState PushArray(string[] indexNames) => new(_arrayIndexNames.Push(indexNames), _enumRounds);
    public RoundState PopArray() => new(_arrayIndexNames.Pop(), _enumRounds);

    public RoundState PushEnum((ITypeSymbol enumType, ITypeSymbol underlyingType) type) => new(_arrayIndexNames, _enumRounds.Push(type));
    public RoundState PopEnum() => new(_arrayIndexNames, _enumRounds.Pop());
}

