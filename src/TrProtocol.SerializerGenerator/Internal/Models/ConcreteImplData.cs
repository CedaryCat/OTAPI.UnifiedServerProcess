using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public record ConcreteImplData(
        ProtocolTypeData PolymorphicBaseType,
        PolymorphicImplsData PolymorphicBaseTypeData,
        MemberAccessExpressionSyntax AccessDiscriminator)
    {
    }
}
