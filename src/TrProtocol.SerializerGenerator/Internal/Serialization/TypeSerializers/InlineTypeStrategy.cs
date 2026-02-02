using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Inline-type serialization strategy.
/// Used for types that don't implement serialization interfaces but have a type definition available.
/// </summary>
public class InlineTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;

    private readonly CompilationContext _compilationContext;

    public InlineTypeStrategy(CompilationContext compilationContext) {
        _compilationContext = compilationContext;
    }

    public bool CanHandle(TypeSerializerContext context) {
        if (context.MemberTypeSym is not INamedTypeSymbol namedSym)
            return false;

        return _compilationContext.TryGetTypeDefSyntax(
            context.TypeStr, out _, context.Model.Namespace, context.Model.Imports);
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym as INamedTypeSymbol;
        var parentVar = context.ParentVar;
        var mTypeStr = context.TypeStr;

        // Track nullable members.
        if (parentVar is null && context.IsConditional && memberTypeSym!.IsReferenceType && !context.RoundState.IsArrayRound && !context.RoundState.IsEnumRound) {
            context.MemberNullables.Add(m.MemberName);
        }

        if (!_compilationContext.TryGetTypeDefSyntax(
            mTypeStr, out var tdef, context.Model.Namespace, context.Model.Imports) || tdef is null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedMemberType,
                    m.MemberType.GetLocation(),
                    m.MemberName,
                    context.ModelSym.Name,
                    memberTypeSym!.Name));
        }

        var varName = $"gen_var_{parentVar}_{m.MemberName}";
        seriBlock.WriteLine($"{mTypeStr} {varName} = {memberAccess};");
        deserBlock.WriteLine($"{mTypeStr} {varName} = {(memberTypeSym!.IsUnmanagedType ? "default" : "new ()")};");

        var externalMemberValues = context.ExternalMemberValues;
        foreach (var (memberName, memberValue) in externalMemberValues) {
            seriBlock.WriteLine($"{varName}.{memberName} = _{memberName};");
        }

        var transformedInfo = context.TransformCallback(tdef);
        context.ExpandMembersCallback(
            seriBlock, deserBlock, memberTypeSym!,
            transformedInfo.Members.Select<SerializationExpandContext, (SerializationExpandContext, string?, RoundState)>(m2 => (m2, varName, RoundState.Empty)));

        deserBlock.WriteLine($"{memberAccess} = {varName};");
    }
}
