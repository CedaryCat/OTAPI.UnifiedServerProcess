using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

public class ProtocolModelValidator
{
    public static Dictionary<string, PolymorphicImplsData> ValidatePolymorphic(Dictionary<string, PolymorphicImplsInfo> polymorphicTypes, ProtocolTypeData[] models) {
        Dictionary<string, PolymorphicImplsData> polymorphicModels = [];
        foreach (var model in models) {
            PolymorphicImplsInfo? inheritFrom = null;
            PropertyDeclarationSyntax idMember;
            INamedTypeSymbol modelSym = model.DefSymbol;

            if (modelSym.IsReferenceType && modelSym.TypeKind != TypeKind.Interface) {
                var baseType = modelSym.BaseType;
                if (baseType is not null && baseType.Name != "Object" && polymorphicTypes.TryGetValue(baseType.GetFullName(), out inheritFrom)) {
                    if (inheritFrom is null) {
                        throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG05",
                                    $"abstract invaild model declaration",
                                    "abstract model '{0}' should declarate with attribute '{1}'",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                model.DefSyntax.GetLocation(),
                                baseType,
                                nameof(PolymorphicBaseAttribute)));
                    }
                }

                var interfaces = modelSym.Interfaces
                    .Select(i => polymorphicTypes.TryGetValue(i.GetFullName(), out var info) ? info : null)
                    .Where(i => i is not null)
                    .ToArray();

                if (interfaces.Length > 0) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG05",
                                $"invaild model declaration",
                                "The type {0} inherits from multiple PolymorphicBaseAttribute annotation types simultaneously:",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.BaseList!.GetLocation(),
                            model.TypeName,
                            nameof(PolymorphicBaseAttribute)));
                }
            }
            else {
                var interfaces = modelSym.Interfaces
                    .Select(i => polymorphicTypes.TryGetValue(i.GetFullName(), out var info) ? info : null)
                    .Where(i => i is not null)
                    .OfType<PolymorphicImplsInfo>()
                    .ToArray();

                if (interfaces.Length > 1) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG05",
                                $"invaild model declaration",
                                "The type {0} inherits from multiple PolymorphicBaseAttribute annotation types simultaneously:",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.BaseList!.GetLocation(),
                            model.TypeName,
                            nameof(PolymorphicBaseAttribute)));
                }
                else if (interfaces.Length == 1) {
                    inheritFrom = interfaces[0];
                }
            }

            if (inheritFrom is not null) {
                idMember = model.DefSyntax.Members
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == inheritFrom.discriminatorPropertyName);

                MemberAccessExpressionSyntax enumMemberAcess;

                if (model.DefSymbol.TypeKind == TypeKind.Class && inheritFrom.type.TypeKind == TypeKind.Class) {
                    if (idMember is null
                        || !idMember.Modifiers.Any(m => m.Text == "override")
                        || !idMember.Modifiers.Any(m => m.Text == "sealed")
                        || idMember.ExpressionBody is not ArrowExpressionClauseSyntax arrow
                        || arrow.Expression is not MemberAccessExpressionSyntax memberAcess
                        || memberAcess.Expression.ToString() != inheritFrom.discriminatorEnum.Name) {

                        throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG06",
                                    $"invaild model declaration",
                                    "model '{0}' should sealed override get accessor of property '{1}' with arrow expression of enum '{2}' constant",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                model.DefSyntax.GetLocation(),
                                model.TypeName,
                                inheritFrom.discriminatorPropertyName,
                                inheritFrom.discriminatorEnum.Name));
                    }
                    enumMemberAcess = memberAcess;
                }
                else if (model.DefSymbol.TypeKind == TypeKind.Class || model.DefSymbol.TypeKind == TypeKind.Struct) {
                    if (idMember is null
                        || idMember.ExpressionBody is not ArrowExpressionClauseSyntax arrow
                        || arrow.Expression is not MemberAccessExpressionSyntax memberAcess
                        || memberAcess.Expression.ToString() != inheritFrom.discriminatorEnum.Name) {

                        throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG06",
                                    $"invaild model declaration",
                                    "model '{0}' should implement get accessor of property '{1}' with arrow expression of enum '{2}' constant",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                model.DefSyntax.GetLocation(),
                                model.TypeName,
                                inheritFrom.discriminatorPropertyName,
                                inheritFrom.discriminatorEnum.Name));
                    }
                    enumMemberAcess = memberAcess;
                }
                else if (model.DefSymbol.TypeKind == TypeKind.Interface) {
                    var implClaim = modelSym.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == nameof(ImplementationClaimAttribute));
                    if (implClaim is null || !model.DefSyntax.AttributeMatch<ImplementationClaimAttribute>(out var implClaimAttribute)) {
                        throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG05",
                                    $"invaild model declaration",
                                    "When the interface '{0}' inherits another interface that is decorated with PolymorphicBaseAttribute, the specific implementation of Ideiscriminator is not declared using the ImplementationClaimAttribute.",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                model.DefSyntax.BaseList!.GetLocation(),
                                model.TypeName,
                                nameof(PolymorphicBaseAttribute)));
                    }
                    var claim = implClaim.ConstructorArguments[0].Type;
                    if (inheritFrom.discriminatorEnum.GetFullName() != claim?.GetFullName()
                        || implClaimAttribute.ArgumentList is null) {
                        throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG05",
                                    $"invaild model declaration",
                                    "The Ideiscriminator declared in the ImplementationClaimAttribute of {0} is not within the type {1} of the Ideiscriminator required by the parent class interface {2}.",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                implClaimAttribute.GetLocation(),
                                model.TypeName,
                                inheritFrom.discriminatorEnum.Name,
                                inheritFrom.type.Name,
                                nameof(PolymorphicBaseAttribute)));
                    }
                    enumMemberAcess = implClaimAttribute.ArgumentList.Arguments
                        .Select(a => a.Expression)
                        .OfType<MemberAccessExpressionSyntax>()
                        .FirstOrDefault(a => a.Expression.ToString() == inheritFrom.discriminatorEnum.Name)

                        ?? throw new DiagnosticException(
                            Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "SCG05",
                                    $"invaild model declaration",
                                    "The parameters required by the ImplementationClaimAttribute must be provided by directly accessing the members of the {0}.",
                                    "",
                                    DiagnosticSeverity.Error,
                                    true),
                                implClaimAttribute.GetLocation(),
                                inheritFrom.discriminatorEnum.Name,
                                nameof(PolymorphicBaseAttribute)));

                }
                else {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG06",
                                $"invaild model declaration",
                                "unexpected model '{0}' type '{1}'",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.GetLocation(),
                            model.TypeName,
                            inheritFrom.discriminatorPropertyName,
                            inheritFrom.discriminatorEnum.Name));
                }

                if (!polymorphicModels.TryGetValue(inheritFrom.discriminatorEnum.Name, out var polymorphicData)) {
                    polymorphicModels.Add(inheritFrom.discriminatorEnum.Name, polymorphicData = new(models.First(m => m.TypeName == inheritFrom.type.Name), inheritFrom));
                }
                polymorphicData.Implementations.Add(enumMemberAcess.Name.Identifier.Text, model);
                model.IsConcreteImpl = true;
                model.ConcreteImplData = new ConcreteImplData(model, polymorphicData, enumMemberAcess);
            }

            if (model.IsAbstract) {

                if (!model.DefSyntax.AttributeMatch<PolymorphicBaseAttribute>()) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG07",
                                $"abstract invaild model declaration",
                                "abstract model '{0}' should declarate with attribute '{1}'",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.GetLocation(),
                            model.TypeName,
                            nameof(PolymorphicBaseAttribute)));
                }

                if (model.DefSyntax.BaseList is null || !model.DefSyntax.BaseList.Types.Any(i => i.ToString() is nameof(IAutoSerializable))) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG08",
                                $"abstract invaild model declaration",
                                "abstract model '{0}' should implement interface '{1}'",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.BaseList?.GetLocation() ?? model.DefSyntax.GetLocation(),
                            model.TypeName,
                            nameof(IAutoSerializable)));
                }

                var info = polymorphicTypes[modelSym.GetFullName()];
                idMember = model.DefSyntax.Members
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == info.discriminatorPropertyName);
                AccessorDeclarationSyntax accessor_get;

                if (idMember is null || idMember.AccessorList is null || (accessor_get = idMember.AccessorList.Accessors.FirstOrDefault(a => a.Keyword.Text == "get")) is null) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG09",
                                $"abstract invaild model declaration",
                                "abstract model '{0}' should define a abstract property '{1}' of enum '{2}'",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.GetLocation(),
                            model.TypeName,
                            info.discriminatorPropertyName,
                            info.discriminatorEnum.Name));
                }
                else if (idMember.AccessorList.Accessors.Count != 1 || !idMember.Modifiers.Any(m => m.Text == "abstract") || accessor_get.ExpressionBody is not null || accessor_get.Body is not null) {
                    throw new DiagnosticException(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCG10",
                                $"abstract invaild model declaration",
                                "abstract model '{0}' should define a abstract property '{1}' of enum '{2}' with only get accessor",
                                "",
                                DiagnosticSeverity.Error,
                                true),
                            model.DefSyntax.GetLocation(),
                            model.TypeName,
                            info.discriminatorPropertyName,
                            info.discriminatorEnum.Name));
                }
            }

            if (polymorphicTypes.TryGetValue(modelSym.GetFullName(), out var polymorphicInfo)) {
                if (!polymorphicModels.TryGetValue(polymorphicInfo.discriminatorEnum.Name, out var implInfo)) {
                    polymorphicModels.Add(polymorphicInfo.discriminatorEnum.Name, new PolymorphicImplsData(model, polymorphicInfo));
                }
            }
        }
        foreach (var model in models) {
            Stack<ConcreteImplData> stack = new();
            var currentData = model.ConcreteImplData;
            while (currentData is not null && currentData.PolymorphicBaseTypeData.PolymorphicBaseType.IsInterface) {
                currentData = currentData.PolymorphicBaseTypeData.PolymorphicBaseType.ConcreteImplData;
                if (currentData is null) {
                    break;
                }
                stack.Push(currentData);
            }
            while (stack.Count > 0) {
                currentData = stack.Pop();
                var baseData = currentData.PolymorphicBaseTypeData;
                model.AutoDiscriminators.Add((baseData.DiscriminatorEnum, baseData.DiscriminatorPropertyName, currentData.AccessDiscriminator));
            }
        }
        foreach (var polymorphicModel in polymorphicModels.Values) {
            polymorphicModel.PolymorphicBaseType.IsPolymorphic = true;
        }

        var globalIDRootTypes = VaidateGlobalTypeID(polymorphicTypes, polymorphicModels, models);
        AllocateGlobalTypeID(polymorphicTypes, polymorphicModels, globalIDRootTypes);

        return polymorphicModels;
    }
    static void AllocateGlobalTypeID(
        Dictionary<string, PolymorphicImplsInfo> polymorphicInfos,
        Dictionary<string, PolymorphicImplsData> polymorphicImpls,
        ProtocolTypeData[] globalIDRootTypes) {

        foreach (var globalIDRootType in globalIDRootTypes) {
            int id = 0;
            foreach (var impl in EnumerateConcreteImpl(polymorphicInfos, polymorphicImpls, globalIDRootType)) {
                impl.GlobalID = id++;
            }
            globalIDRootType.AllocatedGlobalIDCount = id;
        }
    }
    static IEnumerable<ProtocolTypeData> EnumerateConcreteImpl(
        Dictionary<string, PolymorphicImplsInfo> polymorphicInfos,
        Dictionary<string, PolymorphicImplsData> polymorphicImpls,
        ProtocolTypeData type) {
        if (!type.IsPolymorphic) {
            yield return type;
            yield break;
        }
        else {
            var info = polymorphicInfos[type.DefSymbol.GetFullName()];
            var data = polymorphicImpls[info.discriminatorEnum.Name];
            int index = 0;
            Dictionary<string, int> enumOrder = [];
            foreach (var enumMember in info.discriminatorEnum.GetMembers()) {
                enumOrder[enumMember.Name] = index++;
            }
            foreach (var impl in data.Implementations.OrderBy(x => enumOrder[x.Key]).Select(x => x.Value)) {
                foreach (var child in EnumerateConcreteImpl(polymorphicInfos, polymorphicImpls, impl)) {
                    yield return child;
                }
            }
        }
    }
    static ProtocolTypeData[] VaidateGlobalTypeID(
        Dictionary<string, PolymorphicImplsInfo> polymorphicInfos,
        Dictionary<string, PolymorphicImplsData> polymorphicImpls,
        ProtocolTypeData[] models) {
        var rootTypes = models.Where(m => m.IsPolymorphic && !m.IsConcreteImpl);
        List<ProtocolTypeData> globalIDRootTypes = [];
        foreach (var type in rootTypes) {
            VaidateGlobalTypeID(globalIDRootTypes, polymorphicInfos, polymorphicImpls, type, false);
        }
        return [.. globalIDRootTypes];
    }
    static void VaidateGlobalTypeID(
        List<ProtocolTypeData> globalIDRootTypes,
        Dictionary<string, PolymorphicImplsInfo> polymorphicInfos,
        Dictionary<string, PolymorphicImplsData> polymorphicImpls,
        ProtocolTypeData type,
        bool hasAttribute) {

        if (type.DefSyntax.AttributeMatch<GenerateGlobalIDAttribute>(out var match)) {
            if (hasAttribute) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "SCG10",
                            $"Redundant [GenerateGlobalId] attribute on derived type",
                            "GenerateGlobalIDAttribute should not be applied to type '{0}' derived from another type already marked with GenerateGlobalIDAttribute.",
                            "PolymorphicGlobalId",
                            DiagnosticSeverity.Error,
                            true),
                        match.GetLocation(),
                        type.TypeName));
            }
            hasAttribute = true;
            globalIDRootTypes.Add(type);
        }

        if (hasAttribute && type.IsPolymorphic && type.IsAbstract) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SCG10",
                        $"invaild model declaration",
                        "The interface marked with GenerateGlobalIDAttribute cannot be implemented by an abstract class.",
                        "",
                        DiagnosticSeverity.Error,
                        true),
                    type.DefSyntax.BaseList!.GetLocation(),
                    type.TypeName));
        }

        if (polymorphicInfos.TryGetValue(type.DefSymbol.GetFullName(), out var polymorphicInfo)) {
            var impls = polymorphicImpls[polymorphicInfo.discriminatorEnum.Name];
            foreach (var impl in impls.Implementations) {
                VaidateGlobalTypeID(globalIDRootTypes, polymorphicInfos, polymorphicImpls, impl.Value, hasAttribute);
            }
        }
    }
}