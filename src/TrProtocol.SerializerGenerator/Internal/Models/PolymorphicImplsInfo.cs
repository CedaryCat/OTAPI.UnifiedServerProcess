using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using TrProtocol.SerializerGenerator.Internal.Extensions;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public class PolymorphicImplsInfo
    {
        public PolymorphicImplsInfo(INamedTypeSymbol type, INamedTypeSymbol discriminatorEnum, string discriminatorPropertyName) {
            this.type = type;
            this.discriminatorEnum = discriminatorEnum;
            this.EnumUnderlyingType = discriminatorEnum.EnumUnderlyingType!;
            this.discriminatorPropertyName = discriminatorPropertyName;
        }

        public readonly INamedTypeSymbol type;
        public readonly INamedTypeSymbol discriminatorEnum;
        public readonly string discriminatorPropertyName;
        public readonly INamedTypeSymbol EnumUnderlyingType;

        public string EnumUnderlyingTypeName => EnumUnderlyingType.GetPredifinedName();
    }
}
