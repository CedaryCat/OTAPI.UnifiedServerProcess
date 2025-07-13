using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public class ProtocolTypeData
    {
        public ProtocolTypeData(TypeDeclarationSyntax classDeclaration, INamedTypeSymbol symbol, string name, string nameSpace, string[] imports, SerializationExpandContext[] members) {
            DefSyntax = classDeclaration;
            DefSymbol = symbol;
            TypeName = name;
            Members = members;
            Namespace = nameSpace;
            Imports = new HashSet<string>(imports);
        }
        public readonly INamedTypeSymbol DefSymbol;
        public readonly TypeDeclarationSyntax DefSyntax;

        public string Namespace;
        public HashSet<string> Imports;

        public List<(INamedTypeSymbol EnumType, string DiscriminatorName, MemberAccessExpressionSyntax value)> AutoDiscriminators = new();

        public bool IsValueType => DefSyntax is StructDeclarationSyntax;
        public bool IsAbstract;
        public bool IsInterface => DefSymbol.TypeKind == TypeKind.Interface;

        public bool IsNetPacket;
        public bool IsPolymorphic;
        [MemberNotNullWhen(true, nameof(ConcreteImplData))]
        public bool IsConcreteImpl { get; set; }
        public ConcreteImplData? ConcreteImplData;

        public bool HasExtraData;
        public bool IsSideSpecific;
        public bool IsLengthAware;
        public (string? compressLevel, string? bufferSize) CompressData;
        public bool SpecifyLayout => IsSideSpecific || IsLengthAware || AutoDiscriminators.Count > 0;

        public bool PacketAutoSeri;
        public bool HasSeriInterface;
        public bool PacketManualSeri => !PacketAutoSeri && HasSeriInterface;

        public readonly string TypeName;
        public readonly IReadOnlyList<SerializationExpandContext> Members;

        public int GlobalID = -1;
        public int AllocatedGlobalIDCount = -1;
        public bool IsGlobalIDRoot => AllocatedGlobalIDCount >= 0;
    }
}