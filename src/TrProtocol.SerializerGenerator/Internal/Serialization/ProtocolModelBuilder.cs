using Microsoft.CodeAnalysis;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization
{
    public static class ProtocolModelBuilder
    {
        public static ProtocolTypeData BuildProtocolTypeInfo(CompilationContext context, Dictionary<string, PolymorphicImplsInfo> polymorphicTypes, ProtocolTypeInfo info) {
            var defSyntax = info.ClassDeclaration;
            var typeName = defSyntax.Identifier.Text;
            defSyntax.GetNamespace(out var classes, out var fullNamespace, out var unit);
            if (classes.Length != 1) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        new DiagnosticDescriptor("SCG01", "INetPacket DefSymbol error", "Netpacket '{0}' should be a non-nested class",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    defSyntax.GetLocation(),
                    typeName));
            }

            if (fullNamespace is null) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        new DiagnosticDescriptor("SCG02", "Namespace missing", "Namespace of netpacket '{0}' missing",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    defSyntax.GetLocation(),
                    typeName));
            }

            var Namespace = fullNamespace;
            var imports = unit!.Usings
                .Select(u => u.Name?.ToString())
                .Where(u => u is not null)
                .OfType<string>()
                .ToArray();

            if (!context.TryGetTypeSymbol(typeName, out var modelSym, fullNamespace, Array.Empty<string>())) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "SCG32",
                            "unexcepted type DefSymbol missing",
                            "The type '{0}' cannot be found in compilation",
                            "",
                            DiagnosticSeverity.Error,
                            true),
                        defSyntax.GetLocation(),
                        typeName));
            }
            var model = new ProtocolTypeData(defSyntax, modelSym, typeName, Namespace, imports, info.Members);

            if (modelSym.IsOrInheritFrom(nameof(INetPacket))) {
                model.IsNetPacket = true;
            }

            model.IsAbstract = model.DefSyntax.Modifiers.Any(m => m.Text == "abstract");

            var baseList = model.DefSyntax.BaseList;
            if (baseList is not null && baseList.Types.Any(t => t.ToString() == nameof(IExtraData))) {
                if (!model.IsNetPacket || (!modelSym.IsSealed && !modelSym.IsValueType)) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG03",
                                $"Invaild type DefSymbol",
                                "This interface is only allowed to be inherited by packets of sealed type",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            baseList.Types.First(t => t.ToString() == nameof(IExtraData)).GetLocation()));
                }

                model.HasExtraData = true;
            }

            if (modelSym.AllInterfaces.Any(i => i.Name == nameof(ISideSpecific))) {
                model.IsSideSpecific = true;
            }

            if (modelSym.AllInterfaces.Any(t => t.Name == nameof(ILengthAware))) {
                model.IsLengthAware = true;
            }

            if (modelSym.IsValueType) {
                model.IsAbstract = false;
            }

            var compressAtt = model.DefSyntax.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(a => a.AttributeMatch<CompressAttribute>());
            if (compressAtt is not null) {
                if (!model.IsLengthAware) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG04",
                                $"Invaild type DefSymbol",
                                $"'{nameof(CompressAttribute)}' only use on types or structs implymented interface '{nameof(ILengthAware)}'",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                           compressAtt.GetLocation()));
                }
                model.CompressData = (compressAtt.ArgumentList?.Arguments[0].Expression?.ToString(), compressAtt.ArgumentList?.Arguments[1].Expression?.ToString());
            }


            model.PacketAutoSeri = modelSym.AllInterfaces.Any(t => t.Name == nameof(IAutoSerializable));
            model.HasSeriInterface = modelSym.AllInterfaces.Any(i => i.Name == nameof(IBinarySerializable));

            return model;
        }
    }
}
