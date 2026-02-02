using Microsoft.CodeAnalysis;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Serialization strategy for types that implement IBinarySerializable.
/// </summary>
public class BinarySerializableTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        return context.MemberTypeSym.AllInterfaces
            .Any(i => i.Name == nameof(IBinarySerializable));
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;
        var mTypeStr = context.TypeStr;
        var externalMemberValues = context.ExternalMemberValues;
        var externalMemberValueArgs = context.ExternalMemberValueArgs;

        // Apply external member values.
        if (externalMemberValues.Count > 0) {
            if (memberTypeSym.IsUnmanagedType) {
                seriBlock.WriteLine($"var _temp_{m.MemberName} = {memberAccess};");
                foreach (var (memberName, memberValue) in externalMemberValues) {
                    seriBlock.WriteLine($"_temp_{m.MemberName}.{memberName} = {memberValue};");
                }
                seriBlock.WriteLine($"{memberAccess} = _temp_{m.MemberName};");
            }
            else {
                foreach (var (memberName, memberValue) in externalMemberValues) {
                    seriBlock.WriteLine($"{memberAccess}.{memberName} = {memberValue};");
                }
            }
        }

        seriBlock.WriteLine($"{memberAccess}.WriteContent(ref ptr_current);");
        seriBlock.WriteLine();

        if (!memberTypeSym.IsAbstract) {
            bool isLengthAware = memberTypeSym.AllInterfaces.Any(t => t.Name == nameof(ILengthAware));

            if (isLengthAware) {
                if (memberTypeSym.IsUnmanagedType) {
                    if (externalMemberValues.Count > 0) {
                        var variableName = $"_temp_{m.MemberName}";
                        deserBlock.WriteLine($"var {variableName} = {memberAccess};");
                        foreach (var m2 in externalMemberValues) {
                            deserBlock.WriteLine($"{variableName}.{m2.memberName} = _{m2.memberName};");
                        }
                        deserBlock.WriteLine($"{memberAccess} = {variableName};");
                    }
                    deserBlock.WriteLine($"{memberAccess}.ReadContent(ref ptr_current, ptr_end);");
                }
                else {
                    deserBlock.WriteLine($"{memberAccess} = new (ref ptr_current, ptr_end{externalMemberValueArgs});");
                }
            }
            else {
                if (memberTypeSym.IsUnmanagedType) {
                    if (externalMemberValues.Count > 0) {
                        var variableName = $"_temp_{m.MemberName}";
                        deserBlock.WriteLine($"var {variableName} = {memberAccess};");
                        foreach (var m2 in externalMemberValues) {
                            deserBlock.WriteLine($"{variableName}.{m2.memberName} = _{m2.memberName};");
                        }
                        deserBlock.WriteLine($"{memberAccess} = {variableName};");
                    }
                    deserBlock.WriteLine($"{memberAccess}.ReadContent(ref ptr_current);");
                }
                else {
                    deserBlock.WriteLine($"{memberAccess} = new (ref ptr_current{externalMemberValueArgs});");
                }
            }
            deserBlock.WriteLine();
        }
    }
}
