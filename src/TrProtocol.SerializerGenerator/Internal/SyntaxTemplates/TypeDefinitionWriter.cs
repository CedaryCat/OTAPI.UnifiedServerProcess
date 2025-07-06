using TrProtocol.SerializerGenerator.Internal.Utilities;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

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
            namespaceBlock.Write($"public unsafe partial {typeKind} {typeData.TypeName} ");
            return namespaceBlock.BlockWrite((classNode) => { });
        }
    }
}
