using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrProtocol.SerializerGenerator.Internal.Models
{
    public class SerializationExpandContext
    {
        public SerializationExpandContext(MemberDeclarationSyntax memberDeclaration, string name, TypeSyntax type, bool isProp, AttributeListSyntax[] attributeList) :
            this(memberDeclaration, name, type, isProp, attributeList.SelectMany(atts => atts.Attributes)) {
        }
        public SerializationExpandContext(MemberDeclarationSyntax memberDeclaration, string name, TypeSyntax type, bool isProp, IEnumerable<AttributeSyntax> attributes) {
            MemberDeclaration = memberDeclaration;
            MemberName = name;
            if (type is NullableTypeSyntax nullable) {
                MemberType = nullable.ElementType;
                IsNullable = true;
            }
            else {
                MemberType = type;
            }
            IsProperty = isProp;
            Attributes = attributes.ToArray();
        }
        public readonly MemberDeclarationSyntax MemberDeclaration;
        public readonly string MemberName;
        public readonly TypeSyntax MemberType;
        public readonly bool IsProperty;
        public readonly bool IsNullable;
        public bool IsConditional;
        public readonly IReadOnlyList<AttributeSyntax> Attributes;
        public void EnterArrayRound(string[] indexNames) {
            IndexNamesStack.Push(indexNames);
        }
        public void ExitArrayRound() {
            IndexNamesStack.Pop();
        }
        Stack<string[]> IndexNamesStack = new Stack<string[]>();
        public string[] IndexNames => IndexNamesStack.Peek();
        public bool IsArrayRound => IndexNamesStack.Count > 0;

        Stack<(ITypeSymbol enumType, ITypeSymbol underlyingType)> EnumTypes = new Stack<(ITypeSymbol enumType, ITypeSymbol underlyingType)>();
        public (ITypeSymbol enumType, ITypeSymbol underlyingType) EnumType => EnumTypes.Peek();
        public void EnterEnumRound((ITypeSymbol enumType, ITypeSymbol underlyingType) type) => EnumTypes.Push(type);
        public void ExitEnumRound() => EnumTypes.Pop();
        public bool IsEnumRound => EnumTypes.Count > 0;
    }
}