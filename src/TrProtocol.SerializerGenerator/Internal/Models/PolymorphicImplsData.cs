using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public class PolymorphicImplsData
    {
        public PolymorphicImplsData(ProtocolTypeData polymorphicBaseType, PolymorphicImplsInfo info) {
            PolymorphicBaseType = polymorphicBaseType;
            DiscriminatorEnum = info.discriminatorEnum;
            DiscriminatorPropertyName = info.discriminatorPropertyName;
            Info = info;
        }
        public ProtocolTypeData PolymorphicBaseType;
        public INamedTypeSymbol DiscriminatorEnum;
        public string DiscriminatorPropertyName;
        public PolymorphicImplsInfo Info;
        /// <summary>
        /// key: discriminator (enum constant), value: implementation
        /// </summary>
        public readonly Dictionary<string, ProtocolTypeData> Implementations = new();
    }
}
