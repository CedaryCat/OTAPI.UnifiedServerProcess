using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public record ProtocolTypeInfo(TypeDeclarationSyntax ClassDeclaration, string Name, SerializationExpandContext[] Members)
    {
    }
}
