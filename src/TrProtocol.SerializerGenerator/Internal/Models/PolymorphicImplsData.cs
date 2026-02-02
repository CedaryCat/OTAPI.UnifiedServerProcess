using Microsoft.CodeAnalysis;

namespace TrProtocol.SerializerGenerator.Internal.Models;

public class PolymorphicImplsData
{
    public PolymorphicImplsData(ProtocolTypeData polymorphicBaseType, PolymorphicImplsInfo info)
    {
        PolymorphicBaseType = polymorphicBaseType;
        DiscriminatorEnum = info.discriminatorEnum;
        DiscriminatorPropertyName = info.discriminatorPropertyName;
        Is7BitEncoded = info.Is7BitEncoded;
        Info = info;
    }
    public ProtocolTypeData PolymorphicBaseType;
    public INamedTypeSymbol DiscriminatorEnum;
    public string DiscriminatorPropertyName;
    public bool Is7BitEncoded;
    public PolymorphicImplsInfo Info;
    /// <summary>
    /// key: discriminator (enum constant), value: implementation
    /// </summary>
    public readonly Dictionary<string, ProtocolTypeData> Implementations = [];
}
