using Microsoft.CodeAnalysis;
using System.Runtime.InteropServices;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.SyntaxTemplates
{
    public static class TypeDefinitionWriter
    {
        public static BlockNode WriteTypeDefinition(this BlockNode namespaceBlock, ProtocolTypeData typeData) {
            if (!typeData.DefSyntax.AttributeMatch<StructLayoutAttribute>() && typeData.SpecifyLayout) {
                namespaceBlock.WriteLine($"[StructLayout(LayoutKind.Auto)]");
            }
            var typeKind = typeData.DefSymbol.TypeKind switch {
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ => "class",
            };

            string inheritance = "";
            List<string> interfaces = [];
            if (typeData.IsNetPacket) {
                if (!typeData.IsLengthAware) {
                    interfaces.Add(nameof(INonLengthAware));
                }
                if (!typeData.IsSideSpecific) {
                    interfaces.Add(nameof(INonSideSpecific));
                }
                if (!typeData.DefSymbol.IsUnmanagedType) {
                    interfaces.Add(nameof(IManagedPacket));
                }
            }
            if (interfaces.Count > 0) {
                inheritance = $": {string.Join(", ", interfaces)} ";
            }
            namespaceBlock.Write($"public unsafe partial {typeKind} {typeData.TypeName} {inheritance}");
            return namespaceBlock.BlockWrite((classNode) => { });
        }
    }
}
