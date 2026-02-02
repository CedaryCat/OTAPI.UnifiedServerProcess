using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Generation;
using TrProtocol.SerializerGenerator.Internal.Serialization;
using TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

namespace TrProtocol.SerializerGenerator;


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
    private static ProtocolTypeInfo Transform(GeneratorSyntaxContext context, CancellationToken token) {

        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, token) as INamedTypeSymbol;
        if (typeSymbol is not null) {
            return TransformFromSymbol(typeDeclaration, typeSymbol);
        }
        return Transform(typeDeclaration);
    }

    private static ProtocolTypeInfo TransformFromSymbol(TypeDeclarationSyntax triggerDeclaration, INamedTypeSymbol typeSymbol) {
        var orderedMembers = typeSymbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(t => t.Members.Select(m => new {
                Member = m,
                Location = m.GetLocation(),
            }))
            .OrderBy(m => m.Location.SourceTree?.FilePath ?? "")
            .ThenBy(m => m.Location.SourceSpan.Start)
            .Select(m => m.Member)
            .ToArray();

        var members = orderedMembers.Where(m => m.Modifiers.Any(m => m.Text == "public")).Select(new Func<MemberDeclarationSyntax, IEnumerable<SerializationExpandContext>>(m => {
            if (m is FieldDeclarationSyntax field && !field.Modifiers.Any(m => m.Text == "const")) {
                return field.Declaration.Variables.Select(v => new SerializationExpandContext(field, v.Identifier.Text, field.Declaration.Type, false, field.AttributeLists.ToArray()));
            }
            else if (m is PropertyDeclarationSyntax prop) {
                if (prop.AccessorList is null) {
                    return [];
                }
                foreach (var name in new string[] { "get", "set" }) {
                    var access = prop.AccessorList.Accessors.FirstOrDefault(a => a.Keyword.Text == name);
                    if (access == null || access.Modifiers.Any(m => m.Text is "private" or "protected")) {
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

        return new ProtocolTypeInfo(triggerDeclaration, typeSymbol.Name, members);
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
                    var access = prop.AccessorList.Accessors.FirstOrDefault(a => a.Keyword.Text == name);
                    if (access == null || access.Modifiers.Any(m => m.Text is "private" or "protected")) {
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

        var typeSerializerDispatcher = TypeSerializerDispatcher.Create(
            abstractTypesSymbols.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            Compilation);
        #endregion

        var models = BuildModels(context, abstractTypesSymbols, data.infos);
        Dictionary<string, PolymorphicImplsData> polymorphicPackets;
        try {
            polymorphicPackets = ProtocolModelValidator.ValidatePolymorphic(abstractTypesSymbols, models.ToArray());
        }
        catch (DiagnosticException de) {
            context.ReportDiagnostic(de.Diagnostic);
            return;
        }

        foreach (var model in models) {
            TypeFileEmitter.Emit(context, Compilation, typeSerializerDispatcher, model, Transform);
        }

        PolymorphicStaticDeserializeEmitter.Emit(context, polymorphicPackets);
    }

    private static List<ProtocolTypeData> BuildModels(
        SourceProductionContext context,
        Dictionary<string, PolymorphicImplsInfo> polymorphicTypes,
        ImmutableArray<ProtocolTypeInfo> infos) {
        var models = new List<ProtocolTypeData>(infos.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < infos.Length; i++) {
            try {
                var model = ProtocolModelBuilder.BuildProtocolTypeInfo(Compilation, polymorphicTypes, infos[i]);
                var key = model.DefSymbol.GetFullName();
                if (seen.Add(key)) {
                    models.Add(model);
                }
            }
            catch (DiagnosticException de) {
                context.ReportDiagnostic(de.Diagnostic);
            }
        }
        return models;
    }

    static CompilationContext Compilation = new CompilationContext();

    public void Initialize(IncrementalGeneratorInitializationContext initContext) {
        initContext.RegisterSourceOutput(initContext.CompilationProvider.WithComparer(Compilation), Compilation.LoadCompilation);


        var classes = initContext.SyntaxProvider.CreateSyntaxProvider(predicate: FilterTypes, transform: Transform).Collect();
        var combine = initContext.CompilationProvider.Combine(classes);
        initContext.RegisterSourceOutput(combine, Execute);
    }
}
