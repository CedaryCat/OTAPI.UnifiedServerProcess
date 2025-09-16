using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Terraria;
using TrProtocol.Attributes;
using TrProtocol.Exceptions;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Serialization;
using TrProtocol.SerializerGenerator.Internal.SyntaxTemplates;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator
{

    [Generator(LanguageNames.CSharp)]
    public partial class SerializeGenerator : IIncrementalGenerator
    {
        private static bool FilterTypes(SyntaxNode syntaxNode, CancellationToken token) {
            if (syntaxNode is not TypeDeclarationSyntax td/* && td.Keyword.ToString() is not "interface" && td.Keyword.ToString() is not "record" && td.BaseList is not null*/) {
                return false;
            }
            if (td.BaseList is null || td.BaseList.Types.Count == 0) {
                return false;
            }
            td.GetNamespace(out _, out var nameSpace, out _);
            if (nameSpace is null) {
                return false;
            }
            return true;
        }

        #region Transform type synatx to data
        private static ProtocolTypeInfo Transform(GeneratorSyntaxContext context, CancellationToken _) {

            var typeDeclaration = (TypeDeclarationSyntax)context.Node;
            return Transform(typeDeclaration);
        }
        private static ProtocolTypeInfo Transform(TypeDeclarationSyntax typeDeclaration) {
            var members = typeDeclaration.Members.Where(m => m.Modifiers.Any(m => m.Text == "public")).Select(new Func<MemberDeclarationSyntax, IEnumerable<SerializationExpandContext>>(m => {

                if (m is FieldDeclarationSyntax field && !field.Modifiers.Any(m => m.Text == "const")) {
                    return field.Declaration.Variables.Select(v => new SerializationExpandContext(field, v.Identifier.Text, field.Declaration.Type, false, field.AttributeLists.ToArray()));
                }
                else if (m is PropertyDeclarationSyntax prop) {
                    if (prop.AccessorList is null) {
                        return [];
                    }
                    foreach (var name in new string[] { "get", "set" }) {
                        var access = prop.AccessorList.Accessors.FirstOrDefault(a => a.Keyword.ToString() == name);
                        if (access == null || access.Modifiers.Count != 0) {
                            return [];
                        }
                    }
                    return [new SerializationExpandContext(prop, prop.Identifier.Text, prop.Type, true, prop.AttributeLists.ToArray())];
                }
                else {
                    return Array.Empty<SerializationExpandContext>();
                }

            })).SelectMany(list => list).Where(m => {

                return !m.Attributes.Any(a => a.AttributeMatch<IgnoreSerializeAttribute>());

            }).ToArray();

            return new ProtocolTypeInfo(typeDeclaration, typeDeclaration.Identifier.Text, members);
        }
        #endregion

        static readonly string[] NeccessaryUsings = [
            "System.Runtime.CompilerServices",
            "System.Runtime.InteropServices",
            "System.Diagnostics.CodeAnalysis",
            "TrProtocol.Attributes",
            "TrProtocol.Interfaces",
            "TrProtocol.Exceptions",
            "TrProtocol.Models",
        ];

        // TODO: Split the serialization code generation process into more smaller SyntaxTemplates
        private static void Execute(SourceProductionContext context, (Compilation compilation, ImmutableArray<ProtocolTypeInfo> infos) data) {
#if DEBUG
            // if (!Debugger.IsAttached) Debugger.Launch();
#endif

            #region Init global info
            Compilation.LoadCompilation(data.compilation);
            var abstractTypesSymbols = Compilation.GetLocalTypesSymbol()
                .OfType<INamedTypeSymbol>()
                .Select(t => t.HasAbstractModelAttribute(out var info) ? (t.GetFullName(), info) : default)
                .Where(t => t != default)
                .ToDictionary(t => t.Item1, t => t.info);
            #endregion

            ProtocolTypeData[] models = new ProtocolTypeData[data.infos.Length];
            for (int i = 0; i < data.infos.Length; i++) {
                try {
                    models[i] = ProtocolModelBuilder.BuildProtocolTypeInfo(Compilation, abstractTypesSymbols, data.infos[i]);
                }
                catch (DiagnosticException de) {
                    context.ReportDiagnostic(de.Diagnostic);
                    continue;
                }
            }
            Dictionary<string, PolymorphicImplsData> polymorphicPackets;
            try {
                polymorphicPackets = ProtocolModelValidator.ValidatePolymorphic(abstractTypesSymbols, models);
            }
            catch (DiagnosticException de) {
                context.ReportDiagnostic(de.Diagnostic);
                return;
            }

            #region Foreach type
            foreach (var model in models) {
                try {
                    var modelSym = model.DefSymbol;
                    if (model.IsPolymorphic || !model.HasSeriInterface || model.IsInterface) {
                        continue;
                    }

                    #region Method:Resolve member symbol <CheckMemberSymbol>
                    static void CheckMemberSymbol(INamedTypeSymbol typeSym, SerializationExpandContext m, out ITypeSymbol mTypeSym, out IFieldSymbol? fieldMemberSym, out IPropertySymbol? propMemberSym) {

                        var fieldsSym = typeSym.GetMembers().OfType<IFieldSymbol>().Where(f => f.DeclaredAccessibility is Accessibility.Public).ToArray();
                        var propertiesSym = typeSym.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility is Accessibility.Public).ToArray();

                        propMemberSym = propertiesSym.FirstOrDefault(p => p.Name == m.MemberName);
                        fieldMemberSym = fieldsSym.FirstOrDefault(f => f.Name == m.MemberName);

                        if (fieldMemberSym is not null && !m.IsProperty) {
                            mTypeSym = fieldMemberSym.Type;
                        }
                        else if (propMemberSym is not null && m.IsProperty) {
                            mTypeSym = propMemberSym.Type;
                        }
                        else {
                            throw new DiagnosticException(
                                Diagnostic.Create(
                                    new DiagnosticDescriptor(
                                        "SCG11",
                                        "unexcepted member DefSymbol missing",
                                        "The member '{0}' of type '{1}' cannot be found in compilation",
                                        "",
                                        DiagnosticSeverity.Error,
                                        true),
                                    m.MemberDeclaration.GetLocation(),
                                    m.MemberName,
                                    typeSym.Name));
                        }
                        if (mTypeSym.Name is nameof(Nullable<byte>)) {
                            throw new DiagnosticException(
                                Diagnostic.Create(
                                    new DiagnosticDescriptor(
                                        "SCG12",
                                        "invaild member DefSymbol",
                                        "Members '{0}' of type '{0}' cannot be null-assignable value types '{2}'",
                                        "",
                                        DiagnosticSeverity.Error,
                                        true),
                                    m.MemberType.GetLocation(),
                                    m.MemberName,
                                    typeSym.Name,
                                    m.MemberType));
                        }
                    }
                    #endregion

                    List<string> memberNullables = new();
                    var file = new SourceCodeWriter(1024 * 4);
                    file.WriteLine();
                    file.WriteLine("// <auto-generated>");
                    var usingTarget = file.WriteLine();
                    file.Write($"namespace {model.Namespace} ");
                    file.BlockWrite((namespaceBlock) => {

                        var classNode = namespaceBlock.WriteTypeDefinition(model);
                        classNode.WriteAutoDiscriminator(model);

                        #region Add Global ID
                        if (model.GlobalID >= 0) {
                            classNode.WriteLine($"public static int GlobalID => {model.GlobalID};");
                        }
                        #endregion

                        #region Add type constructors and special behavior

                        List<(string memberName, string memberType)> externalMembers = [.. model.DefSyntax.Members
                            .Where(m => m.AttributeLists
                            .SelectMany(a => a.Attributes)
                            .Any(a => a.AttributeMatch<ExternalMemberAttribute>()))
                            .Select((m) => {
                                if (m is PropertyDeclarationSyntax prop) {
                                    return new List<(string, string)>() { (prop.Identifier.ToString(), prop.Type.ToString()) };
                                }
                                else {
                                    var field = (FieldDeclarationSyntax)m;
                                    var list = new List<(string memberName, string memberType)>();
                                    foreach (var variable in field.Declaration.Variables) {
                                        list.Add((variable.Identifier.ToString(), field.Declaration.Type.ToString()));
                                    }
                                    return list;
                                }
                            }).SelectMany(m => m)];

                        string externalMemberParams;
                        if (externalMembers.Count == 0) {
                            externalMemberParams = "";
                        }
                        else {
                            externalMemberParams = $", {string.Join(", ", externalMembers.Select(m => $"{m.memberType} _{m.memberName} = default"))}";
                        }
                        if (model.IsSideSpecific) {

                            if (!model.IsNetPacket) {
                                throw new DiagnosticException(
                                    Diagnostic.Create(
                                        new DiagnosticDescriptor(
                                            "SCG13",
                                            $"Invaild type DefSymbol",
                                            $"This interface '{nameof(ISideSpecific)}' is only allowed to be inherited by packets",
                                            "",
                                            DiagnosticSeverity.Error,
                                            true),
                                        model.DefSyntax.GetLocation()));
                            }
                            classNode.WriteLine($"public bool {nameof(ISideSpecific.IsServerSide)} {{ get; set; }}");
                        }

                        if (model.IsLengthAware) {
                            if (model.HasExtraData) {
                                classNode.WriteLine($"public byte[] {nameof(IExtraData.ExtraData)} {{ get; set; }} = [];");
                            }
                            classNode.Write($"public {model.TypeName}(ref void* ptr, void* ptr_end{(model.IsSideSpecific ? ", bool isServerSide" : "")}{externalMemberParams})");
                            classNode.BlockWrite((source) => {
                                source.WriteLine($"ReadContent(ref ptr, ptr_end);");
                            });
                        }
                        else {
                            classNode.Write($"public {model.TypeName}(ref void* ptr{(model.IsSideSpecific ? ", bool isServerSide" : "")}{externalMemberParams})");
                            classNode.BlockWrite((source) => {
                                if (model.IsSideSpecific) {
                                    source.WriteLine($"{nameof(ISideSpecific.IsServerSide)} = isServerSide;");
                                }
                                foreach (var m in externalMembers) {
                                    source.WriteLine($"{m.memberName} = _{m.memberName};");
                                }
                                source.WriteLine($"ReadContent(ref ptr);");
                            });
                        }

                        if (model.PacketAutoSeri) {
                            List<SerializationExpandContext> defaults = new(model.Members.Count);
                            var initMembers = model.Members.Where(m => !m.Attributes.Any(a => a.AttributeMatch<InitNullableAttribute>())).ToArray();
                            int shouldCreateNewConstructor = -1;
                            var parameters = initMembers.Select(m => {
                                if (m.Attributes.Any(a => a.AttributeMatch<InitDefaultValueAttribute>())) {
                                    if (shouldCreateNewConstructor == -1) {
                                        shouldCreateNewConstructor = 0;
                                    }
                                    if (m.IsNullable) {
                                        defaults.Add(m);
                                        return null;
                                    }
                                    if (Compilation.TryGetTypeSymbol(m.MemberType.GetTypeSymbolName(), out var mtsym, model.Namespace, model.Imports) && mtsym.IsUnmanagedType) {
                                        defaults.Add(m);
                                        return null;
                                    }
                                }
                                if (shouldCreateNewConstructor == 0) {
                                    shouldCreateNewConstructor = 1;
                                }
                                return $"{m.MemberType} _{m.MemberName}";
                            }).Where(s => s is not null).ToList();

                            var parameters_extra = new List<string>();
                            if (model.HasExtraData) {
                                parameters_extra.Add($"byte[] _{nameof(IExtraData.ExtraData)}");
                            }
                            if (model.IsSideSpecific) {
                                parameters_extra.Add($"bool _{nameof(ISideSpecific.IsServerSide)}");
                            }
                            var parameters_defaults = defaults.Select(m => $"{m.MemberType} _{m.MemberName} = default").ToList();

                            if (shouldCreateNewConstructor == 1 || (parameters_defaults.Count > 0 && (model.HasExtraData || model.IsSideSpecific))) {
                                classNode.Write($"public {model.TypeName}({string.Join(", ", initMembers.Select(m => $"{m.MemberType} _{m.MemberName}").Concat(parameters_extra))}) ");
                                classNode.BlockWrite((source) => {
                                    if (model.IsSideSpecific) {
                                        source.WriteLine($"this.{nameof(ISideSpecific.IsServerSide)} = _{nameof(ISideSpecific.IsServerSide)};");
                                    }
                                    foreach (var m in initMembers) {
                                        source.WriteLine($"this.{m.MemberName} = _{m.MemberName};");
                                    }
                                    if (model.HasExtraData) {
                                        source.WriteLine($"this.{nameof(IExtraData.ExtraData)} = _{nameof(IExtraData.ExtraData)};");
                                    }
                                });
                            }
                            classNode.Write($"public {model.TypeName}({string.Join(", ", parameters.Concat(parameters_extra).Concat(parameters_defaults))}) ");
                            classNode.BlockWrite((source) => {
                                if (model.IsSideSpecific) {
                                    source.WriteLine($"this.{nameof(ISideSpecific.IsServerSide)} = _{nameof(ISideSpecific.IsServerSide)};");
                                }
                                foreach (var m in initMembers) {
                                    source.WriteLine($"this.{m.MemberName} = _{m.MemberName};");
                                }
                                if (model.HasExtraData) {
                                    source.WriteLine($"this.{nameof(IExtraData.ExtraData)} = _{nameof(IExtraData.ExtraData)};");
                                }
                            });
                        }

                        if (model.PacketManualSeri) {
                            return;
                        }
                        #endregion

                        #region Method:Add serialization condition to members <MemberConditionCheck>
                        void MemberConditionCheck(ITypeSymbol parent, SerializationExpandContext m, ITypeSymbol mType, string? parant_var, BlockNode membersSource, bool seri) {
                            if (!m.IsArrayRound && !m.IsEnumRound) {

                                List<string> conditions = new List<string>();

                                foreach (var conditionGroupAtt in m.MemberDeclaration.AttributeLists) {
                                    List<string> conditionAnd = new List<string>();

                                    foreach (var conditionAtt in conditionGroupAtt.Attributes) {

                                        if (conditionAtt.ArgumentList is null) {
                                            continue;
                                        }

                                        if (conditionAtt.AttributeMatch<ConditionAttribute>()) {
                                            var conditionArgs = conditionAtt.ArgumentList.Arguments;

                                            string conditionMemberName;
                                            string conditionMemberAccess;
                                            string? conditionIndex = null;
                                            bool conditionPred = true;

                                            if (conditionArgs[0].Expression.IsLiteralExpression(out var text1) && text1.StartsWith("\"") && text1.EndsWith("\"")) {
                                                conditionMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = text1[1..^1]);
                                            }
                                            else if (conditionArgs[0].Expression is InvocationExpressionSyntax invo1 && invo1.Expression.ToString() == "nameof") {
                                                conditionMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = invo1.ArgumentList.Arguments.First().Expression.ToString());
                                            }
                                            else {
                                                goto throwException;
                                            }
                                            if (conditionArgs.Count == 3) {
                                                if (conditionArgs[1].Expression.IsLiteralExpression(out var text3) && byte.TryParse(text3, out _)) {
                                                    conditionIndex = text3;
                                                }
                                                else if (conditionArgs[1].Expression is InvocationExpressionSyntax invo2 && invo2.Expression.ToString() == "sizeof") {
                                                    conditionIndex = invo2.ToString();
                                                }
                                                else {
                                                    goto throwException;
                                                }
                                                if (conditionArgs[2].Expression.IsLiteralExpression(out text3) && bool.TryParse(text3, out var pred)) {
                                                    conditionPred = pred;
                                                }
                                                else {
                                                    goto throwException;
                                                }
                                            }
                                            else if (conditionArgs.Count == 2) {
                                                if (conditionArgs[1].Expression.IsLiteralExpression(out var text2)) {
                                                    if (bool.TryParse(text2, out var pred)) {
                                                        conditionPred = pred;
                                                    }
                                                    else if (byte.TryParse(text2, out _)) {
                                                        conditionIndex = text2;
                                                    }
                                                    else {
                                                        goto throwException;
                                                    }
                                                }
                                                else if (conditionArgs[1].Expression is InvocationExpressionSyntax invo2 && invo2.Expression.ToString() == "sizeof") {
                                                    conditionIndex = invo2.ToString();
                                                }
                                                else {
                                                    goto throwException;
                                                }
                                            }
                                            else if (conditionArgs.Count == 1) {
                                                conditionPred = true;
                                            }
                                            else {
                                                goto throwException;
                                            }

                                            var conditionMember = modelSym.GetMembers(conditionMemberName);

                                            if (conditionIndex is not null && conditionMember.OfType<IFieldSymbol>().Select(f => f.Type).Concat(conditionMember.OfType<IPropertySymbol>().Select(p => p.Type)).Any(t => t.Name != nameof(BitsByte))) {
                                                throw new DiagnosticException(
                                                    Diagnostic.Create(
                                                        new DiagnosticDescriptor(
                                                            "SCG14",
                                                            "condition attribute invaild",
                                                            "arg1 of condition attribute must be name of field or property which type is {0}.",
                                                            "",
                                                            DiagnosticSeverity.Error,
                                                            true),
                                                        conditionAtt.GetLocation(),
                                                        nameof(BitsByte)));
                                            }
                                            if (conditionIndex is null && conditionMember.OfType<IFieldSymbol>().Select(f => f.Type).Concat(conditionMember.OfType<IPropertySymbol>().Select(p => p.Type)).Any(t => t.Name != nameof(Boolean))) {
                                                throw new DiagnosticException(
                                                    Diagnostic.Create(
                                                        new DiagnosticDescriptor(
                                                            "SCG15",
                                                            "condition attribute invaild",
                                                            "arg1 of condition attribute must be name of field or property which type is {0}.",
                                                            "",
                                                            DiagnosticSeverity.Error,
                                                            true),
                                                        conditionAtt.GetLocation(),
                                                        nameof(Boolean)));
                                            }

                                            conditionAnd.Add($"{(conditionPred ? "" : "!")}{conditionMemberAccess}{(conditionIndex is null ? "" : $"[{conditionIndex}]")}");

                                            continue;

                                        throwException:
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG16",
                                                        "condition attribute invaild",
                                                        "condition attribute argument of member '{0}' model '{1}' is invaild.",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    conditionAtt.GetLocation(),
                                                    m.MemberName,
                                                    model.TypeName));
                                        }

                                        else if (conditionAtt.AttributeMatch<ConditionLookupMatchAttribute>() || conditionAtt.AttributeMatch<ConditionLookupNotMatchAttribute>()) {
                                            var conditionArgs = conditionAtt.ArgumentList.Arguments;

                                            string lookupTableName;

                                            string conditionMemberName;
                                            string loopupKeyMemberAccess;

                                            bool conditionPred = true;

                                            if (conditionArgs[0].Expression.IsLiteralExpression(out var text1) && text1.StartsWith("\"") && text1.EndsWith("\"")) {
                                                lookupTableName = text1[1..^1];
                                            }
                                            else if (conditionArgs[0].Expression is InvocationExpressionSyntax invo1 && invo1.Expression.ToString() == "nameof") {
                                                lookupTableName = invo1.ArgumentList.Arguments.First().Expression.ToString();
                                            }
                                            else {
                                                goto throwException;
                                            }

                                            if (conditionArgs[1].Expression.IsLiteralExpression(out var text2) && text2.StartsWith("\"") && text2.EndsWith("\"")) {
                                                loopupKeyMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = text2[1..^1]);
                                            }
                                            else if (conditionArgs[1].Expression is InvocationExpressionSyntax invo2 && invo2.Expression.ToString() == "nameof") {
                                                loopupKeyMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = invo2.ArgumentList.Arguments.First().Expression.ToString());
                                            }
                                            else {
                                                goto throwException;
                                            }

                                            if (conditionArgs.Count == 3) {
                                                if (conditionArgs[2].Expression.IsLiteralExpression(out var text3) && bool.TryParse(text3, out var pred)) {
                                                    conditionPred = pred;
                                                }
                                                else {
                                                    goto throwException;
                                                }
                                            }

                                            conditionAnd.Add($"{(conditionPred ? "" : "!")}{lookupTableName}[{loopupKeyMemberAccess}]");

                                            continue;
                                        throwException:
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG16",
                                                        "condition attribute invaild",
                                                        "condition attribute argument of member '{0}' model '{1}' is invaild.",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    conditionAtt.GetLocation(),
                                                    m.MemberName,
                                                    model.TypeName));
                                        }

                                        (string? condiOperator, int mode) = conditionAtt.Name.ToString() switch {
                                            nameof(ConditionEqualAttribute) or "ConditionEqual" => ("==", 0),
                                            nameof(ConditionNotEqualAttribute) or "ConditionNotEqual" => ("!=", 0),
                                            nameof(ConditionGreaterThanAttribute) or "ConditionGreaterThan" => (">", 0),
                                            nameof(ConditionGreaterThanEqualAttribute) or "ConditionGreaterThanEqual" => (">=", 0),
                                            nameof(ConditionLessThanAttribute) or "ConditionLessThan" => ("<", 0),
                                            nameof(ConditionLessThanEqualAttribute) or "ConditionLessThanEqual" => ("<=", 0),

                                            nameof(ConditionLookupEqualAttribute) or "ConditionLookupEqual" => ("==", 1),
                                            nameof(ConditionLookupNotEqualAttribute) or "ConditionLookupNotEqual" => ("!=", 1),
                                            _ => default,
                                        };

                                        if (condiOperator is not null) {
                                            var conditionArgs = conditionAtt.ArgumentList.Arguments;

                                            string lookupTableName;

                                            string conditionMemberName;
                                            string conditionLeftValue;
                                            string conditionRightValue;

                                            int leftArgIndex = mode == 0 ? 0 : 1;
                                            int rightArgIndex = mode == 0 ? 1 : 2;

                                            if (conditionArgs[leftArgIndex].Expression.IsLiteralExpression(out var leftText) && leftText.StartsWith("\"") && leftText.EndsWith("\"")) {
                                                conditionLeftValue = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = leftText[1..^1]);
                                            }
                                            else if (conditionArgs[leftArgIndex].Expression is InvocationExpressionSyntax invo && invo.Expression.ToString() == "nameof") {
                                                conditionLeftValue = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = invo.ArgumentList.Arguments.First().Expression.ToString());
                                            }
                                            else {
                                                goto throwException;
                                            }

                                            if (mode == 1) {
                                                if (conditionArgs[0].Expression.IsLiteralExpression(out var lookupTableText) && lookupTableText.StartsWith("\"") && lookupTableText.EndsWith("\"")) {
                                                    lookupTableName = lookupTableText[1..^1];
                                                }
                                                else if (conditionArgs[0].Expression is InvocationExpressionSyntax invo && invo.Expression.ToString() == "nameof") {
                                                    lookupTableName = invo.ArgumentList.Arguments.First().Expression.ToString();
                                                }
                                                else {
                                                    goto throwException;
                                                }
                                                conditionLeftValue = $"{lookupTableName}[{conditionLeftValue}]";
                                            }

                                            bool shouldCast = mode == 0;

                                            if (conditionArgs[rightArgIndex].Expression.IsLiteralExpression(out var rightText) && int.TryParse(rightText, out _)) {
                                                conditionRightValue = rightText;
                                            }
                                            else if (conditionArgs[rightArgIndex].Expression is PrefixUnaryExpressionSyntax pu && pu.OperatorToken.Text == "-" && pu.Operand.IsLiteralExpression(out var oppositeText) && int.TryParse(oppositeText, out _)) {
                                                conditionRightValue = pu.ToString();
                                            }
                                            else if (conditionArgs[rightArgIndex].Expression is InvocationExpressionSyntax invo && invo.Expression.ToString() == "sizeof") {
                                                conditionRightValue = invo.ToString();
                                            }
                                            else if (conditionArgs[rightArgIndex].Expression is MemberAccessExpressionSyntax enumAccess) {
                                                shouldCast = false;
                                                conditionRightValue = enumAccess.ToString();
                                            }
                                            else {
                                                goto throwException;
                                            }

                                            string castText = "";
                                            if (shouldCast) {
                                                var conditionMember = modelSym.GetMembers(conditionMemberName);
                                                var castType = conditionMember.OfType<IFieldSymbol>().Select(f => f.Type).Concat(conditionMember.OfType<IPropertySymbol>().Select(p => p.Type)).FirstOrDefault();

                                                if (castType is null) {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG17",
                                                                "condition attribute invaild",
                                                                "arg1 of condition attribute must be name of field or property",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            conditionAtt.GetLocation()));
                                                }

                                                if (castType.AllInterfaces.Any(i => i.Name is nameof(IConvertible))) {
                                                    castText = $"({castType})";
                                                }
                                            }

                                            conditionAnd.Add($"{conditionLeftValue} {condiOperator} {castText}{conditionRightValue}");

                                            continue;

                                        throwException:
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG18",
                                                        "condition attribute invaild",
                                                        "condition attribute argument of member '{0}' model '{1}' is invaild.",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    conditionAtt.GetLocation(),
                                                    m.MemberName,
                                                    model.TypeName));
                                        }
                                    }

                                    if (conditionAnd.Count == 1) {
                                        conditions.Add(conditionAnd[0]);
                                    }
                                    else if (conditionAnd.Count > 1) {
                                        conditions.Add($"({string.Join(" && ", conditionAnd)})");
                                    }
                                }

                                if (conditions.Count != 0) {

                                    m.IsConditional = true;

                                    if (!mType.IsUnmanagedType && mType.NullableAnnotation != NullableAnnotation.Annotated) {

                                        throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG19",
                                                    "Array rank size invaild",
                                                    "Reference type member '{0}' marked as conditional serializations must be declared nullable",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                m.MemberType.GetLocation(),
                                                m.MemberName));
                                    }

                                    membersSource.WarpBlock(($"if ({string.Join(" || ", conditions)}) ", false));
                                }

                                string? serverSideConditionOp = null;
                                AttributeSyntax? another = null;
                                if (m.Attributes.Any(a => a.AttributeMatch<C2SOnlyAttribute>())) {
                                    serverSideConditionOp = seri ? "!" : "";
                                    another = m.Attributes.FirstOrDefault(a => a.AttributeMatch<S2COnlyAttribute>());
                                }
                                else if (m.Attributes.Any(a => a.AttributeMatch<S2COnlyAttribute>())) {
                                    serverSideConditionOp = seri ? "" : "!";
                                    another = m.Attributes.FirstOrDefault(a => a.AttributeMatch<C2SOnlyAttribute>());
                                }
                                if (another is not null) {
                                    throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG03",
                                                $"Invaild member DefSymbol",
                                                $"Only one of C2SOnly and S2COnly can exist at the same time",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            another.GetLocation()));
                                }
                                if (serverSideConditionOp is not null) {
                                    if (!parent.AllInterfaces.Any(i => i.Name == nameof(ISideSpecific))) {
                                        throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG03",
                                                $"Invaild member DefSymbol",
                                                $"C2SOnly and S2COnly can only be annotated on members of types that implement the ISideSpecific interface",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            m.MemberDeclaration.GetLocation()));
                                    }

                                    membersSource.WarpBlock(($"if ({serverSideConditionOp}{nameof(ISideSpecific.IsServerSide)}) ", false));
                                }
                            }

                            if (m.IsArrayRound && !m.IsEnumRound) {
                                var arrConditAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ConditionArrayAttribute>());
                                if (arrConditAtt is not null && arrConditAtt.ArgumentList is not null) {

                                    if (m.IndexNames.Length != 1) {
                                        throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG20",
                                                    "invaild array DefSymbol",
                                                    "ArrayConditionAttribute is only allowed to be applied to members of the one-dimensional array type",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                m.MemberType.GetLocation()));
                                    }

                                    var conditionArgs = arrConditAtt.ArgumentList.Arguments;

                                    string conditionMemberName;
                                    string conditionMemberAccess;
                                    string conditionIndex;
                                    bool conditionPred = true;

                                    if (conditionArgs[0].Expression.IsLiteralExpression(out var text1) && text1.StartsWith("\"") && text1.EndsWith("\"")) {
                                        conditionMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = text1[1..^1]);
                                    }
                                    else if (conditionArgs[0].Expression is InvocationExpressionSyntax invo1 && invo1.Expression.ToString() == "nameof") {
                                        conditionMemberAccess = (parant_var is null ? "" : $"{parant_var}.") + (conditionMemberName = invo1.ArgumentList.Arguments.First().Expression.ToString());
                                    }
                                    else {
                                        goto throwException;
                                    }
                                    if (conditionArgs[1].Expression.IsLiteralExpression(out var text3) && byte.TryParse(text3, out _)) {
                                        conditionIndex = text3;
                                    }
                                    else if (conditionArgs[1].Expression is InvocationExpressionSyntax invo2 && invo2.Expression.ToString() == "sizeof") {
                                        conditionIndex = invo2.ToString();
                                    }
                                    else {
                                        goto throwException;
                                    }
                                    if (conditionArgs.Count == 3) {
                                        if (conditionArgs[2].Expression.IsLiteralExpression(out text3) && bool.TryParse(text3, out var pred)) {
                                            conditionPred = pred;
                                        }
                                        else {
                                            goto throwException;
                                        }
                                    }

                                    string condiExpression;
                                    if (conditionIndex.ToString() == "0") {
                                        condiExpression = m.IndexNames[0];
                                    }
                                    else {
                                        condiExpression = $"{m.IndexNames[0]} + {conditionIndex}";
                                    }

                                    membersSource.WarpBlock(($"if ({(conditionPred ? "" : "!")}{conditionMemberAccess}[{condiExpression}]) ", false));
                                    return;

                                throwException:
                                    throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG21",
                                                "array condition attribute invaild",
                                                "array condition attribute argument of member '{0}' model '{1}' is invaild.",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            arrConditAtt.GetLocation(),
                                            m.MemberName,
                                            model.TypeName));
                                }
                            }
                        }
                        #endregion

                        #region Method:

                        static List<(string memberName, string value)> GetExternalDefaultValue(SerializationExpandContext m, ITypeSymbol memberTypeSym) {
                            List<(string memberName, string memberValue)> defInnerMemberValueAssigns = new();
                            foreach (var defMemberValueAttr in m.Attributes.Where(a => a.AttributeMatch<ExternalMemberValueAttribute>())) {
                                var attrParams = defMemberValueAttr.ExtractAttributeParams();
                                string memberName;
                                string value;
                                if (attrParams[0].IsLiteralExpression(out var text1) && text1.StartsWith("\"") && text1.EndsWith("\"")) {
                                    memberName = text1[1..^1];
                                }
                                else if (attrParams[0] is InvocationExpressionSyntax invo1 && invo1.Expression.ToString() == "nameof") {
                                    memberName = invo1.ArgumentList.Arguments.First().Expression.ToString();
                                }
                                else {
                                    throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG16",
                                                "condition attribute invaild",
                                                "condition attribute argument of member '{0}' is invaild.",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            defMemberValueAttr.GetLocation(),
                                            m.MemberName));
                                }

                                memberName = memberName.Split('.').Last();

                                if (attrParams[1].IsLiteralExpression(out var text2)) {
                                    if (text2.StartsWith("\"") && text2.EndsWith("\"")) {
                                        value = text2;
                                    }
                                    else if (bool.TryParse(text2, out var pred)) {
                                        value = pred.ToString().ToLower();
                                    }
                                    else if (long.TryParse(text2, out var num)) {
                                        value = num.ToString();
                                    }
                                    else if (double.TryParse(text2, out var num2)) {
                                        value = num2.ToString();
                                    }
                                    else {
                                        throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG16",
                                                    "condition attribute invaild",
                                                    "condition attribute argument of member '{0}' model is invaild.",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                defMemberValueAttr.GetLocation(),
                                                m.MemberName));
                                    }
                                }
                                else if (attrParams[1] is InvocationExpressionSyntax invo2) {
                                    if (invo2.Expression.ToString() == "nameof") {
                                        value = $"\"{invo2.ArgumentList.Arguments.First().Expression}\"";
                                    }
                                    else if (invo2.Expression.ToString() == "sizeof") {
                                        value = invo2.ToString();
                                    }
                                    else {
                                        throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG16",
                                                    "condition attribute invaild",
                                                    "condition attribute argument of member '{0}' model is invaild.",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                defMemberValueAttr.GetLocation(),
                                                m.MemberName));
                                    }
                                }
                                else {
                                    throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG16",
                                                    "condition attribute invaild",
                                                    "condition attribute argument of member '{0}' model is invaild.",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                defMemberValueAttr.GetLocation(),
                                                m.MemberName));
                                }
                                ITypeSymbol type = memberTypeSym;
                                if (memberTypeSym is IArrayTypeSymbol arrTypeSym) {
                                    type = arrTypeSym.ElementType;
                                }
                                var ms = type.GetMembers();
                                var innerDVField = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.Name == memberName);
                                var innerDVProp = type.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(f => f.Name == memberName);

                                if (innerDVField is null && innerDVProp is null) {
                                    throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG34",
                                                "unexcepted member DefSymbol",
                                                "Cannot find the default member value '{0}' in the type {1} of this member '{2}'",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            m.MemberType.GetLocation(),
                                            memberName,
                                            type.Name,
                                            m.MemberName));
                                }

                                if (innerDVField is not null) {

                                    if (!innerDVField.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(ExternalMemberAttribute))) {
                                        throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG35",
                                                "unexcepted member DefSymbol",
                                                "Only members decorated with {0} can be set as default values externally with {1}",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            m.MemberType.GetLocation(),
                                            nameof(ExternalMemberAttribute),
                                            nameof(ExternalMemberValueAttribute)));
                                    }

                                    defInnerMemberValueAssigns.Add((innerDVField.Name, value));
                                }
                                else if (innerDVProp is not null) {
                                    if (!innerDVProp.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(ExternalMemberAttribute))) {
                                        throw new DiagnosticException(
                                        Diagnostic.Create(
                                            new DiagnosticDescriptor(
                                                "SCG35",
                                                "unexcepted member DefSymbol",
                                                "Only members decorated with {0} can be set as default values externally with {1}",
                                                "",
                                                DiagnosticSeverity.Error,
                                                true),
                                            m.MemberType.GetLocation(),
                                            nameof(ExternalMemberAttribute),
                                            nameof(ExternalMemberValueAttribute)));
                                    }

                                    defInnerMemberValueAssigns.Add((innerDVProp.Name, value));
                                }
                            }
                            return defInnerMemberValueAssigns;
                        }

                        int indexID = 0;
                        void ExpandMembers(BlockNode seriNode, BlockNode deserNode, INamedTypeSymbol typeSym, IEnumerable<(SerializationExpandContext m, string? parant_var)> memberAccesses) {

                            try {
                                foreach (var (m, parant_var) in memberAccesses) {
                                    var mType = m.MemberType;
                                    var mTypeStr = mType.ToString();
                                    CheckMemberSymbol(typeSym, m, out var memberTypeSym, out var fieldMemberSym, out var propMemberSym);

                                    string memberAccess;

                                    if (parant_var == null) {
                                        memberAccess = m.MemberName;
                                    }
                                    else {
                                        memberAccess = $"{parant_var}.{m.MemberName}";

                                        var memberNameSpace = memberTypeSym.GetFullNamespace();
                                        if (!string.IsNullOrEmpty(memberNameSpace) && !model.Imports.Contains(memberNameSpace)) {
                                            model.Imports.Add(memberNameSpace);
                                        }
                                    }

                                    if (m.IsArrayRound) {
                                        mType = ((ArrayTypeSyntax)mType).ElementType;
                                        memberTypeSym = ((IArrayTypeSymbol)memberTypeSym).ElementType;

                                        if (mType is NullableTypeSyntax) {
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG22",
                                                        "invaild array element DefSymbol",
                                                        "The element type of an array type member '{0}' of type '{1}' cannot be nullable '{2}'",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    mType.GetLocation(),
                                                    m.MemberName,
                                                    model.TypeName,
                                                    mType.ToString()));
                                        }

                                        mTypeStr = mType.ToString();
                                        memberAccess = $"{memberAccess}[{string.Join(",", m.IndexNames)}]";
                                    }
                                    if (m.IsEnumRound) {
                                        mTypeStr = m.EnumType.underlyingType.GetPredifinedName();
                                    }
                                    var seriMemberBlock = new BlockNode(seriNode);
                                    var deserMemberBlock = new BlockNode(deserNode);

                                    var typeAsAttr = fieldMemberSym?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == nameof(SerializeAsAttribute));
                                    typeAsAttr ??= propMemberSym?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == nameof(SerializeAsAttribute));

                                    INamedTypeSymbol? serializeAsType = null;
                                    if (typeAsAttr is not null) {
                                        m.MemberDeclaration.AttributeMatch<SerializeAsAttribute>(out var att);
                                        if (att is null) {
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG22",
                                                        "invaild SerializeAsAttribute usage",
                                                        "Inner exception in SerializeAsAttribute",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    m.MemberDeclaration.GetLocation()));
                                        }

                                        if (!memberTypeSym.IsNumber(true) && mType is not ArrayTypeSyntax) {
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG22",
                                                        "invaild SerializeAsAttribute usage",
                                                        "The SerializeAsAttribute of member '{0}' of type '{1}' can only be used on number types, enums and number arrays",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    att.GetLocation(),
                                                    model.TypeName,
                                                    m.MemberName));
                                        }

                                        if (typeAsAttr.ConstructorArguments.Length == 0) {
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG22",
                                                        "invaild SerializeAsAttribute usage",
                                                        "The SerializeAsAttribute of member '{0}' of type '{1}' must have an argument",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    att.GetLocation(),
                                                    model.TypeName,
                                                    m.MemberName));
                                        }

                                        serializeAsType = typeAsAttr.ConstructorArguments[0].Value as INamedTypeSymbol
                                        ?? throw new DiagnosticException(
                                            Diagnostic.Create(
                                                new DiagnosticDescriptor(
                                                    "SCG22",
                                                    "invaild SerializeAsAttribute usage",
                                                    "The SerializeAsAttribute of member '{0}' of type '{1}' must have a type argument",
                                                    "",
                                                    DiagnosticSeverity.Error,
                                                    true),
                                                att.GetLocation(),
                                                model.TypeName,
                                                m.MemberName));

                                        if (!serializeAsType.IsNumber()) {
                                            throw new DiagnosticException(
                                                Diagnostic.Create(
                                                    new DiagnosticDescriptor(
                                                        "SCG22",
                                                        "invaild SerializeAsAttribute usage",
                                                        "The SerializeAsAttribute of member '{0}' of type '{1}' must have a number type argument",
                                                        "",
                                                        DiagnosticSeverity.Error,
                                                        true),
                                                    att.GetLocation(),
                                                    model.TypeName,
                                                    m.MemberName));
                                        }
                                    }

                                    switch (mTypeStr) {
                                        case "byte":
                                        case nameof(Byte):
                                        case "sbyte":
                                        case nameof(SByte):
                                        case "ushort":
                                        case nameof(UInt16):
                                        case "short":
                                        case nameof(Int16):
                                        case "uint":
                                        case nameof(UInt32):
                                        case "int":
                                        case nameof(Int32):
                                        case "ulong":
                                        case nameof(UInt64):
                                        case "long":
                                        case nameof(Int64):
                                        case "float":
                                        case nameof(Single):
                                        case "double":
                                        case nameof(Double):
                                        case "decimal":
                                        case nameof(Decimal):
                                            if (serializeAsType is not null) {
                                                var typeStr = serializeAsType.GetPredifinedName();
                                                seriMemberBlock.WriteLine($"Unsafe.Write(ptr_current, ({typeStr}){memberAccess});");
                                                seriMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{typeStr}>(ptr_current, 1);");
                                                seriMemberBlock.WriteLine();

                                                deserMemberBlock.WriteLine($"{memberAccess} = ({mTypeStr})Unsafe.Read<{typeStr}>(ptr_current);");
                                                deserMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{typeStr}>(ptr_current, 1);");
                                                deserMemberBlock.WriteLine();
                                            }
                                            else {
                                                seriMemberBlock.WriteLine($"Unsafe.Write(ptr_current, {(m.IsEnumRound ? $"({mTypeStr})" + memberAccess : memberAccess)});");
                                                seriMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
                                                seriMemberBlock.WriteLine();

                                                deserMemberBlock.WriteLine($"{memberAccess} = {(m.IsEnumRound ? $"({m.EnumType.enumType.Name})" : "")}Unsafe.Read<{mTypeStr}>(ptr_current);");
                                                deserMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
                                                deserMemberBlock.WriteLine();
                                            }
                                            goto nextMember;
                                        case "object":
                                        case nameof(Object):
                                            goto nextMember;
                                        case "bool":
                                        case nameof(Boolean):
                                            seriMemberBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess} ? (byte)1 : (byte)0);");
                                            seriMemberBlock.WriteLine($"ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                                            seriMemberBlock.WriteLine();


                                            deserMemberBlock.WriteLine($"{memberAccess} = Unsafe.Read<byte>(ptr_current) != 0;");
                                            deserMemberBlock.WriteLine($"ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                                            deserMemberBlock.WriteLine();
                                            goto nextMember;
                                        case "string":
                                        case nameof(String):

                                            if (parant_var is null && m.IsConditional && !m.IsArrayRound && !m.IsEnumRound) {
                                                memberNullables.Add(m.MemberName);
                                            }
                                            seriMemberBlock.WriteLine($"CommonCode.WriteString(ref ptr_current, {memberAccess});");
                                            seriMemberBlock.WriteLine();


                                            deserMemberBlock.WriteLine($"{memberAccess} = CommonCode.ReadString(ref ptr_current);");
                                            deserMemberBlock.WriteLine();
                                            goto nextMember;
                                        default:

                                            List<(string memberName, string memberValue)> externalMemberValues = GetExternalDefaultValue(m, memberTypeSym);
                                            string externalMemberValueArgs = "";
                                            foreach (var (memberName, memberValue) in externalMemberValues) {
                                                externalMemberValueArgs += $", _{memberName}: {memberValue}";
                                            }
                                            if (mType is ArrayTypeSyntax arr) {

                                                var eleSym = ((IArrayTypeSymbol)memberTypeSym).ElementType;

                                                if (parant_var is null && m.IsConditional && !m.IsArrayRound && !m.IsEnumRound) {
                                                    memberNullables.Add(m.MemberName);
                                                }

                                                if (arr.RankSpecifiers.Count != 1 || m.IsArrayRound) {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG23",
                                                                "Array element should not be array",
                                                                "in netpacket '{0}', element of array '{1}' is a new array mType",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            m.MemberDeclaration.GetLocation(),
                                                            model.TypeName,
                                                            m.MemberName));
                                                }

                                                var arrAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ArraySizeAttribute>()) ??
                                                throw new DiagnosticException(
                                                    Diagnostic.Create(
                                                        new DiagnosticDescriptor(
                                                            "SCG24",
                                                            "Array size attribute missing",
                                                            "Array mType memberAccesses '{0}' of netpacket '{1}' missing size introduction",
                                                            "",
                                                            DiagnosticSeverity.Error,
                                                            true),
                                                        m.MemberDeclaration.GetLocation(),
                                                        m.MemberName,
                                                        model.TypeName));

                                                var indexExps = arrAtt.ExtractAttributeParams();
                                                if (indexExps.Length != arr.RankSpecifiers[0].Rank) {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG25",
                                                                "Array rank conflict",
                                                                "rank of array size attribute is not match with the real array '{0}' at netpacket '{1}'",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            m.MemberDeclaration.GetLocation(),
                                                            m.MemberName,
                                                            model.TypeName));
                                                }
                                                bool elementRepeating = eleSym.AllInterfaces.Any(i => i.Name == nameof(IRepeatElement<int>));
                                                if (elementRepeating && arr.RankSpecifiers[0].Rank != 1) {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG26",
                                                                "Array rank conflict",
                                                                $"Arrays that implement '{nameof(IRepeatElement<int>)}' must be one-dimensional",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            m.MemberDeclaration.GetLocation()));
                                                }
                                                object GetArraySize(ExpressionSyntax indexExp, int i) {
                                                    object? size = null;
                                                    if (indexExp is LiteralExpressionSyntax lit) {
                                                        var text = lit.Token.Text;
                                                        if (text.StartsWith("\"") && text.EndsWith("\"")) {
                                                            size = (parant_var is null ? "" : $"{parant_var}.") + text.Substring(1, text.Length - 2);
                                                        }
                                                        else if (ushort.TryParse(text, out var numSize)) {
                                                            size = numSize;
                                                        }
                                                    }
                                                    else if (indexExp is InvocationExpressionSyntax inv) {
                                                        if (inv.Expression.ToString() == "nameof") {
                                                            size = (parant_var is null ? "" : $"{parant_var}.") + inv.ArgumentList.Arguments.First().Expression;
                                                        }
                                                    }
                                                    if (size == null) {
                                                        throw new DiagnosticException(
                                                            Diagnostic.Create(
                                                                new DiagnosticDescriptor(
                                                                    "SCG27",
                                                                    "Array rank size invaild",
                                                                    "given size of array rank is invaild, index '{0}' of '{1}' at netpacket '{2}'",
                                                                    "",
                                                                    DiagnosticSeverity.Error,
                                                                    true),
                                                                m.MemberDeclaration.GetLocation(),
                                                                i,
                                                                m.MemberName,
                                                                model.TypeName));
                                                    }
                                                    return size;
                                                }

                                                if (elementRepeating) {
                                                    indexID += 1;
                                                    var oldMemberNodeCount = deserMemberBlock.Sources.Count;
                                                    seriMemberBlock.Write($"for (int _g_index_{indexID} = 0; _g_index_{indexID} < {memberAccess}.Length; _g_index_{indexID}++) ");
                                                    (seriMemberBlock, deserMemberBlock).BlockWrite((seriMemberBlock, deserMemberBlock) => {
                                                        m.EnterArrayRound([$"_g_index_{indexID}"]);
                                                        ExpandMembers(seriMemberBlock, deserMemberBlock, typeSym, [(m, parant_var)]);
                                                        m.ExitArrayRound();
                                                    });
                                                    deserMemberBlock.Sources.RemoveRange(oldMemberNodeCount, deserMemberBlock.Sources.Count);
                                                    seriMemberBlock.WriteLine();


                                                    deserMemberBlock.WriteLine($"var _g_elementCount_{indexID} = {GetArraySize(indexExps.First(), 0)};");
                                                    deserMemberBlock.WriteLine($"var _g_arrayCache_{indexID} = ArrayPool<{eleSym.Name}>.Shared.Rent(_g_elementCount_{indexID});");
                                                    deserMemberBlock.WriteLine($"var _g_arrayIndex_{indexID} = 0;");
                                                    deserMemberBlock.Write($"while(_g_elementCount_{indexID} > 0) ");
                                                    deserMemberBlock.BlockWrite((source) => {
                                                        if (eleSym.IsValueType) {
                                                            source.WriteLine($"_g_arrayCache_{indexID}[_g_arrayIndex_{indexID}] = default;");
                                                            foreach (var m in externalMembers) {
                                                                source.WriteLine($"_g_arrayCache_{indexID}[_g_arrayIndex_{indexID}].{m.memberName} = _{m.memberName};");
                                                            }
                                                            source.WriteLine($"_g_arrayCache_{indexID}[_g_arrayIndex_{indexID}].ReadContent(ref ptr_current);");
                                                        }
                                                        else {
                                                            source.WriteLine($"_g_arrayCache_{indexID}[_g_arrayIndex_{indexID}] = new (ref ptr_current);");
                                                        }
                                                        source.WriteLine($"_g_elementCount_{indexID} -= _g_arrayCache_{indexID}[_g_arrayIndex_{indexID}].{nameof(IRepeatElement<>.RepeatCount)} + 1;");
                                                        source.WriteLine($"++_g_arrayIndex_{indexID};");
                                                    });
                                                    deserMemberBlock.WriteLine();
                                                    deserMemberBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[_g_arrayIndex_{indexID}];");
                                                    deserMemberBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexID}, {memberAccess}, _g_arrayIndex_{indexID});");
                                                    deserMemberBlock.WriteLine();
                                                    goto nextMember;
                                                }
                                                else {
                                                    string[] indexNames = new string[indexExps.Length];
                                                    object[] rankSizes = new object[indexExps.Length];
                                                    for (int i = 0; i < indexNames.Length; i++) {
                                                        indexID += 1;
                                                        indexNames[i] = $"_g_index_{indexID}";
                                                        rankSizes[i] = GetArraySize(indexExps[i], i);
                                                    }
                                                    BlockNode? writeArrayBlock = null;
                                                    BlockNode? readArrayBlock = null;
                                                    int index = indexExps.Length - 1;
                                                    do {
                                                        var indexName = indexNames[index];
                                                        var indexExp = indexExps[index];
                                                        var head = $"for (int {indexName} = 0; {indexName} < {rankSizes[index]}; {indexName}++) ";
                                                        if (writeArrayBlock is null || readArrayBlock is null) {
                                                            writeArrayBlock = new BlockNode(seriMemberBlock);
                                                            readArrayBlock = new BlockNode(deserMemberBlock);
                                                            m.EnterArrayRound(indexNames);
                                                            ExpandMembers(writeArrayBlock, readArrayBlock, typeSym, [(m, parant_var)]);
                                                            m.ExitArrayRound();
                                                        }
                                                        readArrayBlock.WarpBlock((head, false));
                                                        writeArrayBlock.WarpBlock((head, false));
                                                    }
                                                    while (index-- > 0);
                                                    seriMemberBlock.Sources.AddRange(writeArrayBlock.Sources);

                                                    deserMemberBlock.WriteLine($"{memberAccess} = new {arr.ElementType}[{string.Join(", ", rankSizes)}];");
                                                    deserMemberBlock.Sources.AddRange(readArrayBlock.Sources);
                                                }
                                                seriMemberBlock.WriteLine();
                                                goto nextMember;
                                            }

                                            if (parant_var is null && m.IsConditional && memberTypeSym.IsReferenceType && !m.IsArrayRound && !m.IsEnumRound) {
                                                memberNullables.Add(m.MemberName);
                                            }


                                            if (memberTypeSym.IsAbstract) {
                                                var mTypefullName = memberTypeSym.GetFullName();
                                                if (abstractTypesSymbols.ContainsKey(mTypefullName)) {
                                                    deserMemberBlock.WriteLine($"{memberAccess} = {memberTypeSym.Name}.Read{memberTypeSym.Name}(ref ptr_current{externalMemberValueArgs});");
                                                }
                                                else {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG28",
                                                                "unexcepted abstract member type",
                                                                "abstract type '{0}' of model '{1}' member '{2}' should defined in current assembly.",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            m.MemberType.GetLocation(),
                                                            memberTypeSym.Name,
                                                            typeSym.Name,
                                                            m.MemberName));
                                                }
                                            }

                                            if (memberTypeSym.AllInterfaces.Any(i => i.Name == nameof(IPackedSerializable))) {
                                                if (!memberTypeSym.IsUnmanagedType) {
                                                    throw new DiagnosticException(
                                                        Diagnostic.Create(
                                                            new DiagnosticDescriptor(
                                                                "SCG29",
                                                                "unexcepted type DefSymbol",
                                                                "Only unmanaged types can implement this interface.",
                                                                "",
                                                                DiagnosticSeverity.Error,
                                                                true),
                                                            m.MemberType.GetLocation()));
                                                }
                                                seriMemberBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess});");
                                                seriMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
                                                seriMemberBlock.WriteLine();

                                                if (!memberTypeSym.IsAbstract) {
                                                    deserMemberBlock.WriteLine($"{memberAccess} = Unsafe.Read<{mTypeStr}>(ptr_current);");
                                                    deserMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{mTypeStr}>(ptr_current, 1);");
                                                    deserMemberBlock.WriteLine();
                                                }
                                                goto nextMember;
                                            }

                                            if (memberTypeSym.AllInterfaces.Any(i => i.Name == nameof(IBinarySerializable))) {

                                                if (externalMemberValues.Count > 0) {
                                                    if (memberTypeSym.IsUnmanagedType) {
                                                        seriMemberBlock.WriteLine($"var _temp_{m.MemberName} = {memberAccess};");
                                                        foreach (var (memberName, memberValue) in externalMemberValues) {
                                                            seriMemberBlock.WriteLine($"_temp_{m.MemberName}.{memberName} = {memberValue};");
                                                        }
                                                        seriMemberBlock.WriteLine($"{memberAccess} = _temp_{m.MemberName};");
                                                    }
                                                    else {
                                                        foreach (var (memberName, memberValue) in externalMemberValues) {
                                                            seriMemberBlock.WriteLine($"{memberAccess}.{memberName} = {memberValue};");
                                                        }
                                                    }
                                                }

                                                seriMemberBlock.WriteLine($"{memberAccess}.WriteContent(ref ptr_current);");
                                                seriMemberBlock.WriteLine();


                                                if (!memberTypeSym.IsAbstract) {
                                                    if (memberTypeSym.AllInterfaces.Any(t => t.Name == nameof(ILengthAware))) {

                                                        if (!typeSym.AllInterfaces.Any(t => t.Name == nameof(ILengthAware))) {
                                                            throw new DiagnosticException(
                                                                Diagnostic.Create(
                                                                    new DiagnosticDescriptor(
                                                                        "SCG30",
                                                                        "unexcepted member type",
                                                                        $"Members that implement '{nameof(ILengthAware)}' must be defined within a type that also implements '{nameof(ILengthAware)}'.",
                                                                        "",
                                                                        DiagnosticSeverity.Error,
                                                                        true),
                                                                    m.MemberType.GetLocation()));
                                                        }

                                                        if (memberTypeSym.IsUnmanagedType) {
                                                            if (externalMemberValues.Count > 0) {
                                                                var variableName = $"_temp_{m.MemberName}";
                                                                deserMemberBlock.WriteLine($"var {variableName} = {memberAccess};");
                                                                foreach (var m2 in externalMemberValues) {
                                                                    namespaceBlock.WriteLine($"{variableName}.{m2.memberName} = _{m2.memberName};");
                                                                }
                                                                deserMemberBlock.WriteLine($"{memberAccess} = {variableName};");
                                                            }
                                                            deserMemberBlock.WriteLine($"{memberAccess}.ReadContent(ref ptr_current, ptr_end);");
                                                        }
                                                        else {
                                                            deserMemberBlock.WriteLine($"{memberAccess} = new (ref ptr_current, ptr_end{externalMemberValueArgs});");
                                                        }
                                                    }
                                                    else {
                                                        if (memberTypeSym.IsUnmanagedType) {
                                                            if (externalMemberValues.Count > 0) {
                                                                var variableName = $"_temp_{m.MemberName}";
                                                                deserMemberBlock.WriteLine($"var {variableName} = {memberAccess};");
                                                                foreach (var m2 in externalMemberValues) {
                                                                    namespaceBlock.WriteLine($"{variableName}.{m2.memberName} = _{m2.memberName};");
                                                                }
                                                                deserMemberBlock.WriteLine($"{memberAccess} = {variableName};");
                                                            }
                                                            deserMemberBlock.WriteLine($"{memberAccess}.ReadContent(ref ptr_current);");
                                                        }
                                                        else {
                                                            deserMemberBlock.WriteLine($"{memberAccess} = new (ref ptr_current{externalMemberValueArgs});");
                                                        }
                                                    }
                                                    deserMemberBlock.WriteLine();
                                                }
                                                goto nextMember;
                                            }

                                            var seqType = memberTypeSym.AllInterfaces.FirstOrDefault(i => i.Name == nameof(ISerializableView<>))?.TypeArguments.First();
                                            if (seqType != null) {
                                                seriMemberBlock.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}.{nameof(ISerializableView<>.View)});");
                                                seriMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{seqType.GetFullTypeName()}>(ptr_current, 1);");
                                                seriMemberBlock.WriteLine();


                                                var seqTypeName = seqType.GetFullTypeName();
                                                if (m.IsProperty) {
                                                    var varName = $"gen_var_{parant_var}_{m.MemberName}";
                                                    deserMemberBlock.WriteLine($"{seqTypeName} {varName} = {(seqType.IsUnmanagedType ? "default" : "new ()")};");

                                                    deserMemberBlock.WriteLine($"{varName}.{nameof(ISerializableView<>.View)} = Unsafe.Read<{seqTypeName}>(ptr_current);");
                                                    deserMemberBlock.WriteLine($"{memberAccess} = {varName};");
                                                }
                                                else {
                                                    deserMemberBlock.WriteLine($"{memberAccess}.{nameof(ISerializableView<>.View)} = Unsafe.Read<{seqTypeName}>(ptr_current);");
                                                }
                                                deserMemberBlock.WriteLine($"ptr_current = Unsafe.Add<{seqTypeName}>(ptr_current, 1);");
                                                deserMemberBlock.WriteLine();
                                                goto nextMember;
                                            }
                                            else if (memberTypeSym is INamedTypeSymbol { EnumUnderlyingType: not null } namedEnumTypeSym) {
                                                m.EnterEnumRound((memberTypeSym, namedEnumTypeSym.EnumUnderlyingType));
                                                ExpandMembers(seriMemberBlock, deserMemberBlock, typeSym, [(m, parant_var)]);
                                                m.ExitEnumRound();
                                                goto nextMember;
                                            }
                                            else {
                                                if (memberTypeSym is INamedTypeSymbol namedSym) {

                                                    if (Compilation.TryGetTypeDefSyntax(mTypeStr, out var tdef, model.Namespace, model.Imports) && tdef is not null) {
                                                        var varName = $"gen_var_{parant_var}_{m.MemberName}";
                                                        seriMemberBlock.WriteLine($"{mTypeStr} {varName} = {memberAccess};");

                                                        deserMemberBlock.WriteLine($"{mTypeStr} {varName} = {(namedSym.IsUnmanagedType ? "default" : "new ()")};");

                                                        foreach (var (memberName, memberValue) in externalMemberValues) {
                                                            seriMemberBlock.WriteLine($"{varName}.{memberName} = _{memberName};");
                                                        }

                                                        ExpandMembers(seriMemberBlock, deserMemberBlock, namedSym, Transform(tdef).Members.Select<SerializationExpandContext, (SerializationExpandContext, string?)>(m => (m, varName)));

                                                        deserMemberBlock.WriteLine($"{memberAccess} = {varName};");
                                                        goto nextMember;
                                                    }
                                                }
                                                throw new DiagnosticException(
                                                    Diagnostic.Create(
                                                        new DiagnosticDescriptor(
                                                            "SCG31",
                                                            "unexcepted member type",
                                                            "Generating an inline member '{0}' serialization method of {1} encountered a member type {2} that could not generate a resolution.",
                                                            "",
                                                            DiagnosticSeverity.Error,
                                                            true),
                                                        m.MemberType.GetLocation(),
                                                        m.MemberName,
                                                        typeSym.Name,
                                                        memberTypeSym));
                                            }
                                    }

                                nextMember:
                                    MemberConditionCheck(typeSym, m, memberTypeSym, parant_var, seriMemberBlock, true);
                                    seriNode.Sources.AddRange(seriMemberBlock.Sources);
                                    MemberConditionCheck(typeSym, m, memberTypeSym, parant_var, deserMemberBlock, false);
                                    deserNode.Sources.AddRange(deserMemberBlock.Sources);
                                }
                            }
                            catch (DiagnosticException de) {
                                context.ReportDiagnostic(de.Diagnostic);
                                return;
                            }
                        }
                        #endregion

                        #region WriteContent

                        var writeNode = new BlockNode(classNode);
                        var readNode = new BlockNode(classNode);

                        writeNode.WriteLine("var ptr_current = ptr;");
                        writeNode.WriteLine();

                        if (model.IsConcreteImpl) {
                            INamedTypeSymbol[] ExtractAbstractModelInheritance(INamedTypeSymbol type) {
                                var abstractModelAncestors = type.GetFullInheritanceTree()
                                    .Where(t => t.HasAbstractModelAttribute())
                                    .OfType<INamedTypeSymbol>()
                                    .ToList();

                                var graph = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
                                foreach (var ancestor in abstractModelAncestors) {
                                    var directParents = ancestor.GetFullInheritanceTree()
                                        .Intersect(abstractModelAncestors, SymbolEqualityComparer.Default)
                                        .OfType<INamedTypeSymbol>()
                                        .ToList();

                                    graph[ancestor] = directParents;
                                }

                                var roots = graph.Where(kvp => !kvp.Value.Any()).Select(kvp => kvp.Key).ToList();
                                if (roots.Count > 1) {
                                    throw new InvalidOperationException(
                                        $"Type {type.Name} inherits from multiple independent AbstractModelAttribute marked types: " +
                                        string.Join(", ", roots.Select(r => r.Name)));
                                }

                                foreach (var kvp in graph) {
                                    if (kvp.Value.Count > 1) {
                                        throw new InvalidOperationException(
                                            $"Type {type.Name} ancestor {kvp.Key.Name} inherits from multiple AbstractModelAttribute marked types: " +
                                            string.Join(", ", kvp.Value.Select(p => p.Name)));
                                    }
                                }

                                if (roots.Count == 1) {
                                    // find the closest abstract ancestor (leaf) that does not appear in any other node's parents
                                    var leaf = graph.Keys
                                        .FirstOrDefault(k => !graph.Values.SelectMany(v => v).Contains(k, SymbolEqualityComparer.Default));

                                    var chain = new List<INamedTypeSymbol>();
                                    var current = leaf;

                                    // follow the parent pointer up (child -> parent), and then reverse the chain to get from root to leaf
                                    while (current != null) {
                                        chain.Add(current);

                                        var parent = graph[current].FirstOrDefault(); // safe: graph has entry for current
                                        current = parent;
                                    }

                                    chain.Reverse();
                                    return [];
                                }

                                return [];
                            }

                            foreach (var inherit in ExtractAbstractModelInheritance(modelSym)) {
                                if (inherit.HasAbstractModelAttribute(out var info)) {
                                    writeNode.WriteLine($"Unsafe.Write(ptr_current, ({info.EnumUnderlyingTypeName}){info.discriminatorPropertyName});");
                                    writeNode.WriteLine($"ptr_current = Unsafe.Add<{info.EnumUnderlyingTypeName}>(ptr_current, 1);");
                                    writeNode.WriteLine();
                                }
                            }
                        }


                        readNode.WriteLine();

                        if (model.CompressData != default) {
                            writeNode.WriteLine($"var ptr_compressed = ptr_current;");
                            writeNode.WriteLine($"var rawBuffer = ArrayPool<byte>.Shared.Rent({model.CompressData.bufferSize});");
                            writeNode.Write("fixed (void* ptr_rawdata = rawBuffer)");

                            readNode.WriteLine($"var decompressedBuffer = ArrayPool<byte>.Shared.Rent({model.CompressData.bufferSize});");
                            readNode.Write("fixed (void* ptr_decompressed = decompressedBuffer)");

                            (writeNode, readNode).BlockWrite((writeNode, readNode) => {
                                writeNode.WriteLine("ptr_current = ptr_rawdata;");

                                readNode.WriteLine("var ptr_current = ptr_decompressed;");
                                readNode.WriteLine($"CommonCode.ReadDecompressedData(ptr, ref ptr_current, (int)((long)ptr_end - (long)ptr));");
                                readNode.WriteLine("ptr_current = ptr_decompressed;");

                                ExpandMembers(writeNode, readNode, modelSym, model.Members.Select<SerializationExpandContext, (SerializationExpandContext, string?)>(m => (m, null)));
                                writeNode.WriteLine($"CommonCode.WriteCompressedData(ptr_rawdata, ref ptr_compressed, (int)((long)ptr_current - (long)ptr_rawdata), {model.CompressData.compressLevel});");
                                writeNode.WriteLine($"ArrayPool<byte>.Shared.Return(rawBuffer);");
                                writeNode.WriteLine("ptr = ptr_compressed;");
                            });
                            readNode.WriteLine("ptr = ptr_end;");
                            readNode.WriteLine($"ArrayPool<byte>.Shared.Return(decompressedBuffer);");
                        }
                        else {
                            readNode.WriteLine("var ptr_current = ptr;");

                            ExpandMembers(writeNode, readNode, modelSym, model.Members.Select<SerializationExpandContext, (SerializationExpandContext, string?)>(m => (m, null)));
                            writeNode.WriteLine("ptr = ptr_current;");
                            readNode.WriteLine();
                            if (model.HasExtraData) {
                                writeNode.WriteLine($"Marshal.Copy({nameof(IExtraData.ExtraData)}, 0, (IntPtr)ptr_current, {nameof(IExtraData.ExtraData)}.Length);");
                                writeNode.WriteLine($"ptr_current = Unsafe.Add<byte>(ptr_current, {nameof(IExtraData.ExtraData)}.Length);");

                                readNode.WriteLine("var restContentSize = (int)((long)ptr_end - (long)ptr_current);");
                                readNode.WriteLine($"{nameof(IExtraData.ExtraData)} = new byte[restContentSize];");
                                readNode.WriteLine($"Marshal.Copy((IntPtr)ptr_current, {nameof(IExtraData.ExtraData)}, 0, restContentSize);");
                                readNode.WriteLine("ptr = ptr_end;");
                            }
                            else {
                                readNode.WriteLine("ptr = ptr_current;");
                            }
                        }

                        if (!model.DefSyntax.Members.Any(m => {
                            if (m is MethodDeclarationSyntax method && method.Identifier.Text == "WriteContent") {
                                var param = method.ParameterList.Parameters;
                                if (
                                param.Count == 1 &&
                                param[0].Type is PointerTypeSyntax pointerType &&
                                pointerType.ElementType.ToString() is "void" &&
                                param[0].Modifiers.Count == 1 &&
                                param[0].Modifiers[0].ToString() is "ref") {
                                    return true;
                                }
                            }
                            return false;
                        })) {
                            classNode.Write($"public unsafe {((model.IsConcreteImpl && !model.IsValueType) ? "override " : "")}void WriteContent(ref void* ptr) ");
                            classNode.Sources.Add(writeNode);
                        }
                        #endregion

                        #region ReadContent

                        if (!model.DefSyntax.Members.Any(m => {
                            if (m is MethodDeclarationSyntax method && method.Identifier.Text == "ReadContent") {
                                var param = method.ParameterList.Parameters;
                                if (
                                param.Count == 1 &&
                                param[0].Type is PointerTypeSyntax pointerType &&
                                pointerType.ElementType.ToString() is "void" &&
                                param[0].Modifiers.Count == 1 &&
                                param[0].Modifiers[0].ToString() is "ref") {
                                    return true;
                                }
                            }
                            return false;
                        })) {

                            if (model.IsLengthAware) {
                                classNode.WriteLine("/// <summary>");
                                classNode.WriteLine("/// This operation is not supported and always throws a System.NotSupportedException.");
                                classNode.WriteLine("/// </summary>");
                                classNode.WriteLine($"[Obsolete]");
                                if ((model.IsConcreteImpl && !model.IsValueType)) {
                                    classNode.WriteLine($"public override void ReadContent(ref void* ptr) => throw new {nameof(NotSupportedException)}();");
                                }
                                else {
                                    classNode.WriteLine($"void {nameof(IBinarySerializable)}.ReadContent(ref void* ptr) => throw new {nameof(NotSupportedException)}();");
                                }
                                classNode.WriteLine($"[MemberNotNull({string.Join(", ", memberNullables.Select(m => $"nameof({m})"))})]");
                                classNode.Write($"public unsafe void ReadContent(ref void* ptr, void* ptr_end) ");
                            }
                            else {
                                classNode.WriteLine($"[MemberNotNull({string.Join(", ", memberNullables.Select(m => $"nameof({m})"))})]");
                                classNode.Write($"public unsafe {((model.IsConcreteImpl && !model.IsValueType) ? "override " : "")}void ReadContent(ref void* ptr) ");
                            }
                            classNode.Sources.Add(readNode);
                        }
                        #endregion
                    });

                    #region Write using
                    foreach (var us in model.Imports.Concat(NeccessaryUsings).Distinct()) {
                        usingTarget.NewLineAfter($"using {us};");
                    }
                    foreach (var staticUsing in model.StaticImports) {
                        usingTarget.NewLineAfter($"using static {staticUsing};");
                    }
                    #endregion

                    context.AddSource($"{modelSym.GetFullName()}.seri.g.cs", SourceText.From(file.ToString(), Encoding.UTF8));
                }
                catch (DiagnosticException de) {
                    context.ReportDiagnostic(de.Diagnostic);
                    continue;
                }
            }
            #endregion

            #region Foreach abstract class and add static deseriailize method
            foreach (var polymophic in polymorphicPackets.Values) {

                var polymorphicBase = polymophic.PolymorphicBaseType;
                var implementations = polymophic.Implementations;
                var enumType = polymophic.DiscriminatorEnum;

                if (polymorphicBase is not null) {
                    polymorphicBase.DefSyntax.GetNamespace(out var classes, out var fullNamespace, out var unit);
                    var usings = unit?.Usings.Select(u => u.Name?.ToString() ?? "").Where(u => u is not "").ToArray() ?? [];

                    var source = new SourceCodeWriter(1024 * 4);
                    source.WriteLine();
                    source.WriteLine("// <auto-generated>");
                    source.WriteLine();

                    foreach (var us in usings.Concat(NeccessaryUsings).Concat(implementations.Values.Select(l => {
                        l.DefSyntax.GetNamespace(out _, out var ns, out _);
                        return ns;
                    })).Distinct()) {
                        source.WriteLine($"using {us};");
                    }


                    List<(string memberName, string memberType)> externalMembers = [.. polymorphicBase.DefSyntax.Members
                        .Where(m => m.AttributeLists
                        .SelectMany(a => a.Attributes)
                        .Any(a => a.AttributeMatch<ExternalMemberAttribute>()))
                        .SelectMany((m) => {
                            if (m is PropertyDeclarationSyntax prop) {
                                return new List<(string, string)>() { (prop.Identifier.ToString(), prop.Type.ToString()) };
                            }
                            else {
                                var field = (FieldDeclarationSyntax)m;
                                var list = new List<(string memberName, string memberType)>();
                                foreach (var variable in field.Declaration.Variables) {
                                    list.Add((variable.Identifier.ToString(), field.Declaration.Type.ToString()));
                                }
                                return list;
                            }
                        })];
                    string externalMemberParams;
                    string externalMemberParamsCall;
                    if (externalMembers.Count == 0) {
                        externalMemberParams = "";
                        externalMemberParamsCall = "";
                    }
                    else {
                        externalMemberParams = $", {string.Join(", ", externalMembers.Select(m => $"{m.memberType} _{m.memberName} = default"))}";
                        externalMemberParamsCall = $", {string.Join(", ", externalMembers.Select(m => $"_{m.memberName}"))}";
                    }
                    source.WriteLine();
                    source.Write($"namespace {fullNamespace} ");
                    source.BlockWrite((source) => {
                        var typeKind = polymorphicBase.IsInterface ? "interface" : polymorphicBase.IsValueType ? "struct" : "class";
                        source.Write($"public unsafe partial {typeKind} {polymorphicBase.TypeName} ");
                        source.BlockWrite((source) => {

                            if (polymorphicBase.IsGlobalIDRoot) {
                                source.WriteLine($"public const int GlobalIDCount = {polymorphicBase.AllocatedGlobalIDCount};");
                                source.WriteLine("public static abstract int GlobalID { get; }");
                            }

                            source.Write($"public unsafe static {polymorphicBase.TypeName} Read{polymorphicBase.TypeName}(ref void* ptr{(polymorphicBase.IsNetPacket ? ", void* ptr_end" : "")}{(polymorphicBase.IsNetPacket ? ", bool isServerSide" : "")}{externalMemberParams}) ");
                            source.BlockWrite((source) => {
                                source.WriteLine($"{enumType.Name} identity = ({enumType.Name})Unsafe.Read<{enumType.EnumUnderlyingType}>(ptr);");
                                source.WriteLine($"ptr = Unsafe.Add<{enumType.EnumUnderlyingType}>(ptr, 1);");
                                source.Write($"switch (identity) ");
                                source.BlockWrite((source) => {
                                    foreach (var enumValue in enumType.GetMembers().OfType<IFieldSymbol>()) {
                                        var match = implementations.FirstOrDefault(a => a.Key == enumValue.Name);
                                        var packet = match.Value;
                                        if (packet is not null) {
                                            if (packet.IsPolymorphic) {
                                                source.WriteLine($"case {enumType.Name}.{enumValue.Name}: return {packet.TypeName}.Read{packet.TypeName}(ref ptr{(polymorphicBase.IsNetPacket ? ", ptr_end" : "")}{(polymorphicBase.IsNetPacket ? ", isServerSide" : "")}{externalMemberParamsCall});");
                                            }
                                            else {
                                                source.WriteLine($"case {enumType.Name}.{enumValue.Name}: return new {packet.TypeName}(ref ptr{(packet.IsLengthAware ? ", ptr_end" : "")}{(packet.IsSideSpecific ? ", isServerSide" : "")}{externalMemberParamsCall});");
                                            }
                                        }
                                    }
                                    source.WriteLine($"default: throw new {nameof(UnknownDiscriminatorException)}(typeof({polymorphicBase.TypeName}), identity, (long)identity);");
                                });
                            });
                        });
                    });
                    context.AddSource($"{polymorphicBase.DefSyntax.GetFullName()}.static.seri.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
                }
            }
            #endregion
        }

        static CompilationContext Compilation = new CompilationContext();

        public void Initialize(IncrementalGeneratorInitializationContext initContext) {
            initContext.RegisterSourceOutput(initContext.CompilationProvider.WithComparer(Compilation), Compilation.LoadCompilation);


            var classes = initContext.SyntaxProvider.CreateSyntaxProvider(predicate: FilterTypes, transform: Transform).Collect();
            var combine = initContext.CompilationProvider.Combine(classes);
            initContext.RegisterSourceOutput(combine, Execute);
        }
    }
}