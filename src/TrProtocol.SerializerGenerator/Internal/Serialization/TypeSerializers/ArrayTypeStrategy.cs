using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using TrProtocol.Attributes;
using TrProtocol.Exceptions;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Serialization;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Array-type serialization strategy.
/// Handles regular arrays, 7-bit-encoded arrays, sparse arrays, and repeat-element arrays.
/// </summary>
public class ArrayTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;

    private readonly CompilationContext _compilationContext;

    public ArrayTypeStrategy(CompilationContext compilationContext) {
        _compilationContext = compilationContext;
    }
    public bool CanHandle(TypeSerializerContext context) {
        return context.Member.MemberType is ArrayTypeSyntax && !context.RoundState.IsArrayRound;
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;
        var parentVar = context.ParentVar;

        var arr = (ArrayTypeSyntax)m.MemberType;
        var eleSym = ((IArrayTypeSymbol)memberTypeSym).ElementType;

        // Track nullable members.
        if (parentVar is null && context.IsConditional && !context.RoundState.IsArrayRound && !context.RoundState.IsEnumRound) {
            context.MemberNullables.Add(m.MemberName);
        }

        // Validate unsupported multi-dimensional arrays.
        if (arr.RankSpecifiers.Count != 1 || context.RoundState.IsArrayRound) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayElementShouldNotBeArray,
                    m.MemberDeclaration.GetLocation(),
                    context.Model.TypeName,
                    m.MemberName));
        }

        // Check for 7-bit encoded arrays.
        if (Int7BitEncodedStrategy.Is7BitEncoded(context)) {
            Generate7BitEncodedArray(context, seriBlock, deserBlock, arr, eleSym);
            return;
        }

        // Check for sparse arrays.
        var sparseAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<SparseArrayAttribute>());
        if (sparseAtt is not null) {
            GenerateSparseArray(context, seriBlock, deserBlock, sparseAtt, eleSym);
            return;
        }

        // Check for sparse arrays.
        var terminatedAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<TerminatedArrayAttribute>());
        if (terminatedAtt is not null) {
            GenerateTerminatedArray(context, seriBlock, deserBlock, terminatedAtt, eleSym);
            return;
        }

        // Regular array handling.
        GenerateRegularArray(context, seriBlock, deserBlock, arr, eleSym);
    }

    private void Generate7BitEncodedArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var eleTypeStr = eleSym.GetPredifinedName();
        bool isIntArray = eleTypeStr == "int" || eleTypeStr == nameof(Int32);
        bool isIntEnumArray = eleSym.TypeKind == TypeKind.Enum
            && ((INamedTypeSymbol)eleSym).EnumUnderlyingType?.SpecialType == SpecialType.System_Int32;

        if (!isIntArray && !isIntEnumArray) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.Int7BitEncodedMemberInvalidType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleTypeStr));
        }

        var arrAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ArraySizeAttribute>())
            ?? throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArraySizeMissing,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName));

        var indexExps = arrAtt.ExtractAttributeParams();
        if (indexExps.Length != 1 || arr.RankSpecifiers[0].Rank != 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.Int7BitEncodedArrayMultiDim,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    arr.RankSpecifiers[0].Rank));
        }

        var arrSize = ExtractSizeExpression(indexExps[0], parentVar);
        var indexId = context.NextIndexId();

        // Serialization
        seriBlock.Write($"for (int _i7b_{indexId} = 0; _i7b_{indexId} < {memberAccess}.Length; _i7b_{indexId}++) ");
        seriBlock.BlockWrite((source) => {
            if (isIntEnumArray)
                source.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, (int){memberAccess}[_i7b_{indexId}]);");
            else
                source.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, {memberAccess}[_i7b_{indexId}]);");
        });
        seriBlock.WriteLine();

        // Deserialization
        deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[{arrSize}];");
        deserBlock.Write($"for (int _i7b_{indexId} = 0; _i7b_{indexId} < {arrSize}; _i7b_{indexId}++) ");
        deserBlock.BlockWrite((source) => {
            if (isIntEnumArray)
                source.WriteLine($"{memberAccess}[_i7b_{indexId}] = ({eleSym.Name})CommonCode.Read7BitEncodedInt(ref ptr_current);");
            else
                source.WriteLine($"{memberAccess}[_i7b_{indexId}] = CommonCode.Read7BitEncodedInt(ref ptr_current);");
        });
        deserBlock.WriteLine();
    }

    private static HashSet<string> supportedPrimitiveTypes = [
        "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double",
        "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double"
    ];

    private void GenerateSparseArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        AttributeSyntax sparseAtt, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var sparseParams = sparseAtt.ExtractAttributeParams();
        if (sparseParams.Length < 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.SparseArrayMissingSize,
                    sparseAtt.GetLocation(),
                    m.MemberName));
        }

        var eleTypeStr = eleSym.GetPredifinedName();
        if (!supportedPrimitiveTypes.Contains(eleTypeStr)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.SparseArrayInvalidType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleTypeStr));
        }

        var sparseSize = ExtractSizeExpression(sparseParams[0], parentVar);
        var sparseTerminator = sparseParams.Length >= 2 && sparseParams[1] is not null
            ? ExtractSizeExpression(sparseParams[1], parentVar)
            : "ushort.MaxValue";

        var indexId = context.NextIndexId();

        // Serialization
        seriBlock.Write($"for (ushort _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {memberAccess}.Length; _sparse_i_{indexId}++) ");
        seriBlock.BlockWrite((source) => {
            source.Write($"if ({memberAccess}[_sparse_i_{indexId}] != default) ");
            source.BlockWrite((inner) => {
                inner.WriteLine($"Unsafe.Write(ptr_current, _sparse_i_{indexId});");
                inner.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
                inner.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_sparse_i_{indexId}]);");
                inner.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
            });
        });
        seriBlock.WriteLine($"Unsafe.Write(ptr_current, (ushort){sparseTerminator});");
        seriBlock.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
        seriBlock.WriteLine();

        // Deserialization
        deserBlock.WriteLine($"{memberAccess} = new {eleTypeStr}[{sparseSize}];");
        deserBlock.Write($"while (true) ");
        deserBlock.BlockWrite((source) => {
            source.WriteLine($"var _sparse_idx_{indexId} = Unsafe.Read<ushort>(ptr_current);");
            source.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
            source.Write($"if (_sparse_idx_{indexId} == {sparseTerminator}) ");
            source.BlockWrite((inner) => {
                inner.WriteLine("break;");
            });
            source.WriteLine($"{memberAccess}[_sparse_idx_{indexId}] = Unsafe.Read<{eleTypeStr}>(ptr_current);");
            source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
        });
        deserBlock.WriteLine();
    }
    private void GenerateTerminatedArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        AttributeSyntax terminatedAtt, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var terminatedParams = terminatedAtt.ExtractAttributeParams();
        if (terminatedParams.Length < 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayMissingMaxCount,
                    terminatedAtt.GetLocation(),
                    m.MemberName));
        }

        var maxSize = ExtractSizeExpression(terminatedParams[0], parentVar);
        var terminatorExpr = terminatedParams.Length >= 2 && terminatedParams[1] is not null
            ? ExtractSizeExpression(terminatedParams[1], parentVar)
            : null;

        var indexId = context.NextIndexId();


        var eleTypeStr = eleSym.GetPredifinedName();
        if (supportedPrimitiveTypes.Contains(eleTypeStr)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {memberAccess}.Length; _sparse_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_sparse_i_{indexId}]);");
                source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({eleTypeStr}){terminator});");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");

            // Small, non-escaping `new` array with known length can be stack-allocated and bounds-checks elided; no need to rent from ArrayPool.
            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {eleSym.Name}[{maxSize}];"); 
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {maxSize}; _sparse_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                source.WriteLine($"var _term_value_{indexId} = Unsafe.Read<{eleTypeStr}>(ptr_current);");
                source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
                source.WriteLine($"if (_term_value_{indexId} == {terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value_{indexId};");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // bool[] is encoded as byte (see BooleanTypeStrategy).
        if (eleTypeStr is "bool" or nameof(Boolean)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_term_i_{indexId}] ? (byte)1 : (byte)0);");
                source.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({terminator}) ? (byte)1 : (byte)0);");
            seriBlock.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new bool[{maxSize}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSize}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                source.WriteLine("var _term_value_byte = Unsafe.Read<byte>(ptr_current);");
                source.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                source.WriteLine("var _term_value = _term_value_byte != 0;");
                source.WriteLine($"if (_term_value == {terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value;");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new bool[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        if (eleTypeStr is "char" or nameof(Char)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_term_i_{indexId}]);");
                source.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, (char){terminator});");
            seriBlock.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new char[{maxSize}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSize}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                source.WriteLine("var _term_value = Unsafe.Read<char>(ptr_current);");
                source.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");
                source.WriteLine($"if (_term_value == (char){terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value;");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new char[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // enum[] is written as its underlying type (see EnumTypeStrategy + PrimitiveTypeStrategy).
        if (eleSym is INamedTypeSymbol { EnumUnderlyingType: not null } enumSym) {
            var enumUnderlying = enumSym.EnumUnderlyingType!;
            var underlyingStr = enumUnderlying.GetPredifinedName();
            if (!supportedPrimitiveTypes.Contains(underlyingStr)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.TerminatedArrayInvalidElementType,
                        m.MemberDeclaration.GetLocation(),
                        m.MemberName,
                        eleTypeStr,
                        eleTypeStr));
            }

            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr}){memberAccess}[_term_i_{indexId}]);");
                source.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr})({terminator}));");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {enumSym.Name}[{maxSize}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSize}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                source.WriteLine($"var _term_value_raw_{indexId} = Unsafe.Read<{underlyingStr}>(ptr_current);");
                source.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
                source.WriteLine($"if (_term_value_raw_{indexId} == ({underlyingStr})({terminator})) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = ({enumSym.Name})_term_value_raw_{indexId};");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new {enumSym.Name}[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // Non-primitive: struct element where the termination key is the first serializable public member.
        if (eleSym is not INamedTypeSymbol eleNamed || !eleNamed.IsValueType) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidElementType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    "struct",
                    eleTypeStr));
        }

        bool hasCustomSerializer = eleNamed.AllInterfaces.Any(i =>
            i.Name == nameof(IPackedSerializable)
            || i.Name == nameof(IBinarySerializable)
            || i.Name == nameof(ISerializableView<>));
        if (hasCustomSerializer) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayElementHasCustomSerializer,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleNamed.Name));
        }

        if (!_compilationContext.TryGetTypeDefSyntax(eleNamed.Name, out var tdef, context.Model.Namespace, context.Model.Imports) || tdef is null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayElementNotInlineExpandable,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleNamed.Name));
        }

        var typeInfo = context.TransformCallback(tdef);
        if (typeInfo.Members.Length == 0) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    "<none>",
                    eleNamed.Name,
                    "<none>"));
        }

        var keyMember = typeInfo.Members[0];
        MemberSymbolResolver.ResolveMemberSymbol(eleNamed, keyMember, out var keyTypeSym, out var keyFieldSym, out var keyPropSym);

        if ((keyFieldSym?.IsStatic ?? false) || (keyPropSym?.IsStatic ?? false)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    "static"));
        }

        if (keyMember.Attributes.Any(a => a.Name.ToString().Contains("Condition", StringComparison.Ordinal))) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    "conditional"));
        }

        // The termination key cannot be SerializeAs: it would desynchronize the key read/compare from the inline layout.
        var keyMemberAttributes = keyFieldSym?.GetAttributes() ?? keyPropSym?.GetAttributes() ?? [];
        if (keyMemberAttributes.Any(a => a.AttributeClass?.Name == "SerializeAsAttribute")) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayKeyMemberSerializeAsNotSupported,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name));
        }

        string keyTerminator = terminatorExpr ?? "default";
        bool keyIsBool = keyTypeSym.Name is nameof(Boolean);
        bool keyIsChar = keyTypeSym.Name is nameof(Char);
        bool keyIsEnum = keyTypeSym is INamedTypeSymbol { EnumUnderlyingType: not null };

        string keyWireTypeStr;
        string keyMemberTypeStr = keyTypeSym.GetPredifinedName();
        string keyCompareExpr;
        Action<BlockNode, string> writeKey;
        Action<BlockNode, string> readKey;

        if (keyIsBool) {
            keyWireTypeStr = "byte";
            keyCompareExpr = keyTerminator;
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, {access} ? (byte)1 : (byte)0);");
                block.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                block.WriteLine($"var {outVar}_byte = Unsafe.Read<byte>(ptr_current);");
                block.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                block.WriteLine($"var {outVar} = {outVar}_byte != 0;");
            };
        }
        else if (keyIsEnum) {
            var keyEnum = (INamedTypeSymbol)keyTypeSym;
            var underlying = keyEnum.EnumUnderlyingType!;
            var underlyingStr = underlying.GetPredifinedName();
            if (!supportedPrimitiveTypes.Contains(underlyingStr)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                        keyMember.MemberDeclaration.GetLocation(),
                        m.MemberName,
                        keyMember.MemberName,
                        eleNamed.Name,
                        keyMemberTypeStr));
            }

            keyWireTypeStr = underlyingStr;
            keyCompareExpr = $"({underlyingStr})({keyTerminator})";
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr}){access});");
                block.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                block.WriteLine($"var {outVar}_raw = Unsafe.Read<{underlyingStr}>(ptr_current);");
                block.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
                block.WriteLine($"var {outVar} = ({keyEnum.Name}){outVar}_raw;");
            };
        }
        else if (keyIsChar || supportedPrimitiveTypes.Contains(keyMemberTypeStr)) {
            keyWireTypeStr = keyMemberTypeStr;
            keyCompareExpr = $"({keyWireTypeStr})({keyTerminator})";
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, {access});");
                block.WriteLine($"ptr_current = Unsafe.Add<{keyWireTypeStr}>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                block.WriteLine($"var {outVar} = Unsafe.Read<{keyWireTypeStr}>(ptr_current);");
                block.WriteLine($"ptr_current = Unsafe.Add<{keyWireTypeStr}>(ptr_current, 1);");
            };
        }
        else {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    keyMemberTypeStr));
        }

        var elementVar = $"_term_ele_{indexId}";
        var keyValueVar = $"_term_key_{indexId}";
        var keyAccess = $"{elementVar}.{keyMember.MemberName}";
        var remainingMembers = typeInfo.Members.Skip(1)
            .Select<SerializationExpandContext, (SerializationExpandContext m, string? parant_var, RoundState roundState)>(m2 => (m2, elementVar, RoundState.Empty));

        // Serialization: write key first, then write remaining members; finally write terminator key only.
        seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
        seriBlock.BlockWrite(source => {
            source.WriteLine($"var {elementVar} = {memberAccess}[_term_i_{indexId}];");
            writeKey(source, keyAccess);

            var dummyDeser = new BlockNode(source);
            context.ExpandMembersCallback(source, dummyDeser, eleNamed, remainingMembers);
        });
        writeKey(seriBlock, $"({keyMemberTypeStr})({keyTerminator})");
        seriBlock.WriteLine();

        // Deserialization
        deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {eleNamed.Name}[{maxSize}];");
        deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
        deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSize}; _term_i_{indexId}++) ");
        deserBlock.BlockWrite(source => {
            readKey(source, keyValueVar);
            source.WriteLine($"if ({(keyIsBool ? keyValueVar : (keyIsEnum ? $"{keyValueVar}_raw" : keyValueVar))} == {keyCompareExpr}) break;");

            source.WriteLine($"{eleNamed.Name} {elementVar} = default;");
            source.WriteLine($"{keyAccess} = {keyValueVar};");

            var dummySeri = new BlockNode(source);
            context.ExpandMembersCallback(dummySeri, source, eleNamed, remainingMembers);

            source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = {elementVar};");
            source.WriteLine($"++_g_arrayIndex_{indexId};");
        });
        deserBlock.WriteLine($"{memberAccess} = new {eleNamed.Name}[_g_arrayIndex_{indexId}];");
        deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
        deserBlock.WriteLine();
    }

    private void GenerateRegularArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;
        var model = context.Model;

        var arrAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ArraySizeAttribute>())
            ?? throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArraySizeMissing,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    model.TypeName));

        var indexExps = arrAtt.ExtractAttributeParams();
        if (indexExps.Length != arr.RankSpecifiers[0].Rank) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayRankConflict,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    model.TypeName));
        }

        bool elementRepeating = eleSym.AllInterfaces.Any(i => i.MetadataName == "IRepeatElement`1");
        if (elementRepeating && arr.RankSpecifiers[0].Rank != 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.RepeatElementArrayRankConflict,
                    m.MemberDeclaration.GetLocation()));
        }

        object[] rankSizes = new object[indexExps.Length];
        for (int i = 0; i < indexExps.Length; i++) {
            rankSizes[i] = GetArraySize(indexExps[i], i, m, model, parentVar);
        }

        if (elementRepeating) {
            GenerateRepeatElementArray(context, seriBlock, deserBlock, arr, eleSym, indexExps, rankSizes);
        }
        else {
            GenerateStandardArray(context, seriBlock, deserBlock, arr, eleSym, indexExps, rankSizes);
        }
    }

    private void GenerateRepeatElementArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym,
        ExpressionSyntax[] indexExps, object[] rankSizes) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;
        var externalMemberValues = context.ExternalMemberValues;

        var indexId = context.NextIndexId();
        var oldMemberNodeCount = deserBlock.Sources.Count;

        seriBlock.Write($"for (int _g_index_{indexId} = 0; _g_index_{indexId} < {memberAccess}.Length; _g_index_{indexId}++) ");
        (seriBlock, deserBlock).BlockWrite((seriBlock, deserBlock) => {
            var roundState = context.RoundState.PushArray([$"_g_index_{indexId}"]);
            context.ExpandMembersCallback(seriBlock, deserBlock, context.ModelSym, [(m, parentVar, roundState)]);
        });
        deserBlock.Sources.RemoveRange(oldMemberNodeCount, deserBlock.Sources.Count);
        seriBlock.WriteLine();

        deserBlock.WriteLine($"var _g_elementCount_{indexId} = {rankSizes[0]};");
        deserBlock.WriteLine($"var _g_arrayCache_{indexId} = ArrayPool<{eleSym.Name}>.Shared.Rent(_g_elementCount_{indexId});");
        deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
        deserBlock.Write($"while(_g_elementCount_{indexId} > 0) ");
        deserBlock.BlockWrite((source) => {
            if (eleSym.IsValueType) {
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = default;");
                foreach (var exm in externalMemberValues) {
                    source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].{exm.memberName} = _{exm.memberName};");
                }
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].ReadContent(ref ptr_current);");
            }
            else {
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = new (ref ptr_current);");
            }
            source.WriteLine($"_g_elementCount_{indexId} -= _g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].RepeatCount + 1;");
            source.WriteLine($"++_g_arrayIndex_{indexId};");
        });
        deserBlock.WriteLine();
        deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[_g_arrayIndex_{indexId}];");
        deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
        deserBlock.WriteLine();
    }

    private void GenerateStandardArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym,
        ExpressionSyntax[] indexExps, object[] rankSizes) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        string[] indexNames = new string[indexExps.Length];
        for (int i = 0; i < indexNames.Length; i++) {
            indexNames[i] = $"_g_index_{context.NextIndexId()}";
        }

        BlockNode? writeArrayBlock = null;
        BlockNode? readArrayBlock = null;
        int index = indexExps.Length - 1;

        do {
            var indexName = indexNames[index];
            var head = $"for (int {indexName} = 0; {indexName} < {rankSizes[index]}; {indexName}++) ";

            if (writeArrayBlock is null || readArrayBlock is null) {
                writeArrayBlock = new BlockNode(seriBlock);
                readArrayBlock = new BlockNode(deserBlock);
                var roundState = context.RoundState.PushArray(indexNames);
                context.ExpandMembersCallback(writeArrayBlock, readArrayBlock, context.ModelSym, [(m, parentVar, roundState)]);
            }
            readArrayBlock.WarpBlock((head, false));
            writeArrayBlock.WarpBlock((head, false));
        }
        while (index-- > 0);

        seriBlock.Sources.AddRange(writeArrayBlock!.Sources);
        deserBlock.WriteLine($"{memberAccess} = new {arr.ElementType}[{string.Join(", ", rankSizes)}];");
        deserBlock.Sources.AddRange(readArrayBlock!.Sources);
        seriBlock.WriteLine();
    }

    private static string ExtractSizeExpression(ExpressionSyntax expr, string? parentVar) {
        if (expr is LiteralExpressionSyntax lit) {
            return lit.ToString();
        }
        if (expr is InvocationExpressionSyntax invo && invo.Expression.ToString() == "nameof") {
            var name = invo.ArgumentList.Arguments.First().Expression.ToString();
            return (parentVar is null ? "" : $"{parentVar}.") + name;
        }
        return expr.ToString();
    }

    private static object GetArraySize(ExpressionSyntax indexExp, int i, SerializationExpandContext m, ProtocolTypeData model, string? parentVar) {
        object? size = null;
        if (indexExp is LiteralExpressionSyntax lit) {
            var text = lit.Token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"")) {
                size = (parentVar is null ? "" : $"{parentVar}.") + text.Substring(1, text.Length - 2);
            }
            else if (ushort.TryParse(text, out var numSize)) {
                size = numSize;
            }
        }
        else if (indexExp is InvocationExpressionSyntax inv && inv.Expression.ToString() == "nameof") {
            size = (parentVar is null ? "" : $"{parentVar}.") + inv.ArgumentList.Arguments.First().Expression;
        }

        if (size == null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayRankSizeInvalid,
                    m.MemberDeclaration.GetLocation(),
                    i,
                    m.MemberName,
                    model.TypeName));
        }
        return size;
    }
}
