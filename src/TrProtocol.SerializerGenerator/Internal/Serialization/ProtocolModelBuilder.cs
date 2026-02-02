using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

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

        if (!context.TryGetTypeSymbol(typeName, out var modelSym, fullNamespace, [])) {
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

        var (imports, staticImports) = CollectImports(modelSym);
        var model = new ProtocolTypeData(defSyntax, modelSym, typeName, Namespace, imports, staticImports, info.Members);

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

        if (modelSym.AllInterfaces.Any(i => i.Name == nameof(INonSideSpecific))) {
            var location = baseList!.GetLocation();
            var foundInterface = baseList!.Types.First(t => t.ToString() == nameof(INonSideSpecific));
            if (foundInterface is not null) {
                location = foundInterface.GetLocation();
            }
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG32",
                        "Do not manually implement INonSideSpecific",
                        "The interface 'INonSideSpecific' is automatically provided by the source generator. Do not declare it explicitly in the inheritance list.",
                        "SourceGeneration",
                        DiagnosticSeverity.Error,
                        true),
                    location));
        }
        if (modelSym.AllInterfaces.Any(i => i.Name == nameof(INonLengthAware))) {
            var location = baseList!.GetLocation();
            var foundInterface = baseList!.Types.First(t => t.ToString() == nameof(INonLengthAware));
            if (foundInterface is not null) {
                location = foundInterface.GetLocation();
            }
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG32",
                        "Do not manually implement INonLengthAware",
                        "The interface 'INonLengthAware' is automatically provided by the source generator. Do not declare it explicitly in the inheritance list.",
                        "SourceGeneration",
                        DiagnosticSeverity.Error,
                        true),
                    location));
        }
        if (modelSym.AllInterfaces.Any(i => i.Name == nameof(IManagedPacket))) {
            var location = baseList!.GetLocation();
            var foundInterface = baseList!.Types.First(t => t.ToString() == nameof(IManagedPacket));
            if (foundInterface is not null) {
                location = foundInterface.GetLocation();
            }
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG32",
                        "Do not manually implement IManagedPacket",
                        "The interface 'IManagedPacket' is automatically provided by the source generator. Do not declare it explicitly in the inheritance list.",
                        "SourceGeneration",
                        DiagnosticSeverity.Error,
                        true),
                    location));
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

    private static (string[] imports, string[] staticImports) CollectImports(INamedTypeSymbol modelSym) {
        var imports = new HashSet<string>();
        var staticImports = new HashSet<string>();

        foreach (var decl in modelSym.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()) {
            decl.GetNamespace(out _, out _, out var unit);
            if (unit is null) {
                continue;
            }

            foreach (var u in unit.Usings.Where(u => u.GlobalKeyword == default)) {
                var name = u.Name?.ToString();
                if (string.IsNullOrEmpty(name)) {
                    continue;
                }
                var nonNullName = name!;

                if (u.StaticKeyword == default) {
                    imports.Add(nonNullName);
                }
                else {
                    staticImports.Add(nonNullName);
                }
            }
        }

        return (imports.ToArray(), staticImports.ToArray());
    }
}
