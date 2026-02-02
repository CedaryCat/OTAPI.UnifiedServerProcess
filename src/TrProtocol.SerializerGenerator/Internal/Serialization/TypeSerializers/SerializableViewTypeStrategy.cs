using Microsoft.CodeAnalysis;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Serialization strategy for types that implement ISerializableView.
/// </summary>
public class SerializableViewTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;
    public bool CanHandle(TypeSerializerContext context) {
        return context.MemberTypeSym.AllInterfaces
            .Any(i => i.Name == nameof(ISerializableView<>));
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;

        var seqType = memberTypeSym.AllInterfaces
            .FirstOrDefault(i => i.Name == nameof(ISerializableView<>))?.TypeArguments.First();

        if (seqType == null) return;

        var seqTypeName = seqType.GetFullTypeName();

        seriBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}.{nameof(ISerializableView<>.View)});");
        seriBlock.WriteLine($"ptr_current = Unsafe.Add<{seqTypeName}>(ptr_current, 1);");
        seriBlock.WriteLine();

        if (m.IsProperty) {
            var varName = $"gen_var_{context.ParentVar}_{m.MemberName}";
            deserBlock.WriteLine($"{seqTypeName} {varName} = {(seqType.IsUnmanagedType ? "default" : "new ()")};");
            deserBlock.WriteLine($"{varName}.{nameof(ISerializableView<>.View)} = Unsafe.Read<{seqTypeName}>(ptr_current);");
            deserBlock.WriteLine($"{memberAccess} = {varName};");
        }
        else {
            deserBlock.WriteLine($"{memberAccess}.{nameof(ISerializableView<>.View)} = Unsafe.Read<{seqTypeName}>(ptr_current);");
        }
        deserBlock.WriteLine($"ptr_current = Unsafe.Add<{seqTypeName}>(ptr_current, 1);");
        deserBlock.WriteLine();
    }
}
