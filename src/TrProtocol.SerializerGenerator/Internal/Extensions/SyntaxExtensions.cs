using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace TrProtocol.SerializerGenerator.Internal.Extensions
{
    public static class SyntaxExtensions
    {
        public static void GetNamespace(this TypeDeclarationSyntax type, out string[] typeParentsNames, out string? fullNameSpce, out CompilationUnitSyntax? compilationUnit) {
            var parent = type.Parent;
            List<string> names = new List<string>() {
                type.Identifier.Text,
            };
            while (parent is ClassDeclarationSyntax classParent) {
                names.Insert(0, classParent.Identifier.Text);
                parent = classParent.Parent;
            }
            typeParentsNames = [.. names];
            fullNameSpce = null;
            while (parent is NamespaceDeclarationSyntax @namespace) {
                if (fullNameSpce is null) {
                    fullNameSpce = @namespace.Name.ToString();
                }
                else {
                    fullNameSpce = @namespace.Name.ToString() + "." + fullNameSpce;
                }
                parent = @namespace.Parent;
            }
            if (parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespace) {
                fullNameSpce = fileScopedNamespace.Name.ToString();
                parent = fileScopedNamespace.Parent;
            }
            compilationUnit = parent as CompilationUnitSyntax;
        }
        public static string GetFullName(this TypeDeclarationSyntax type) {
            type.GetNamespace(out var typeParentsNames, out var ns, out _);
            if (ns is not null) {
                return string.Join(".", new string[] { ns }.Concat(typeParentsNames)) + "." + type.Identifier;
            }
            return string.Join(".", typeParentsNames) + "." + type.Identifier;
        }
        public static bool GetParent<TNode>(this SyntaxNode node, [NotNullWhen(true)] out TNode? result) where TNode : SyntaxNode {
            SyntaxNode? parent = node;
            result = null;
            do {
                parent = parent.Parent;
                if (parent is TNode n) {
                    result = n;
                    return true;
                }
            }
            while (parent is not null);
            return false;
        }
        public static bool IsLiteralExpression(this ExpressionSyntax expression, [NotNullWhen(true)] out string? text) {
            if (expression is LiteralExpressionSyntax lite) {
                text = lite.Token.Text; return true;
            }
            text = null;
            return false;
        }
        public static bool AttributeMatch<TAttribute>(this MemberDeclarationSyntax member) where TAttribute : Attribute {
            return member.AttributeLists.SelectMany(list => list.Attributes).Any(att => att.AttributeMatch<TAttribute>());
        }
        public static bool AttributeMatch<TAttribute>(this MemberDeclarationSyntax member, [NotNullWhen(true)] out AttributeSyntax? match) where TAttribute : Attribute {
            match = member.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(att => att.AttributeMatch<TAttribute>());
            return match is not null;
        }
        public static bool AttributeMatch<TAttribute>(this AttributeSyntax attribute) where TAttribute : Attribute {
            var name = typeof(TAttribute).Name;
            var name1 = attribute.Name.ToString();
            var name2 = attribute.Name.ToString() + "Attribute";
            return name == name1 || name == name2;
        }
        public static ExpressionSyntax[] ExtractAttributeParams(this AttributeSyntax attribute) {
            if (attribute.ArgumentList == null) {
                return [];
            }
            if (attribute.ArgumentList.Arguments.Count == 1 && attribute.ArgumentList.Arguments.First().Expression is InitializerExpressionSyntax init) {
                return [.. init.Expressions];
            }
            else {
                return [.. attribute.ArgumentList.Arguments.Select(a => a.Expression)];
            }
        }
        public static string GetTypeSymbolName(this TypeSyntax type) {
            if (type is PredefinedTypeSyntax predefined) {
                return (predefined.Keyword.Kind()) switch {
                    SyntaxKind.BoolKeyword => nameof(Boolean),
                    SyntaxKind.ByteKeyword => nameof(Byte),
                    SyntaxKind.SByteKeyword => nameof(SByte),
                    SyntaxKind.IntKeyword => nameof(Int32),
                    SyntaxKind.UIntKeyword => nameof(UInt32),
                    SyntaxKind.ShortKeyword => nameof(Int16),
                    SyntaxKind.UShortKeyword => nameof(UInt16),
                    SyntaxKind.LongKeyword => nameof(Int64),
                    SyntaxKind.ULongKeyword => nameof(UInt64),
                    SyntaxKind.FloatKeyword => nameof(Single),
                    SyntaxKind.DoubleKeyword => nameof(Double),
                    SyntaxKind.DecimalKeyword => nameof(Decimal),
                    SyntaxKind.StringKeyword => nameof(String),
                    SyntaxKind.CharKeyword => nameof(Char),
                    SyntaxKind.ObjectKeyword => nameof(Object),
                    SyntaxKind.VoidKeyword => "Void",
                    _ => type.ToString()
                };
            }
            return type.ToString();
        }
    }
}
