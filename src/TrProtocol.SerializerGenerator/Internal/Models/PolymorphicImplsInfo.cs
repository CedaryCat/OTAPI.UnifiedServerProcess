using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Extensions;

namespace TrProtocol.SerializerGenerator.Internal.Models;

public class PolymorphicImplsInfo
{
    public PolymorphicImplsInfo(INamedTypeSymbol type, INamedTypeSymbol discriminatorEnum, string discriminatorPropertyName, bool is7BitEncoded)
    {
        this.type = type;
        this.discriminatorEnum = discriminatorEnum;
        this.EnumUnderlyingType = discriminatorEnum.EnumUnderlyingType!;
        this.discriminatorPropertyName = discriminatorPropertyName;
        this.Is7BitEncoded = is7BitEncoded;
    }

    public readonly INamedTypeSymbol type;
    public readonly INamedTypeSymbol discriminatorEnum;
    public readonly string discriminatorPropertyName;
    public readonly INamedTypeSymbol EnumUnderlyingType;
    public readonly bool Is7BitEncoded;

    public string EnumUnderlyingTypeName => EnumUnderlyingType.GetPredifinedName();
}
