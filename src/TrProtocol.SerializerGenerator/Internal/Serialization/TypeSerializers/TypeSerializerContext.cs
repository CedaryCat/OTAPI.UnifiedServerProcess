using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers
{
    /// <summary>
    /// Type serializer context that bundles all data required by strategies.
    /// </summary>
    public class TypeSerializerContext
    {
        /// <summary>Member context.</summary>
        public SerializationExpandContext Member { get; }

        /// <summary>Member type symbol.</summary>
        public ITypeSymbol MemberTypeSym { get; }

        /// <summary>Field symbol (if any).</summary>
        public IFieldSymbol? FieldMemberSym { get; }

        /// <summary>Property symbol (if any).</summary>
        public IPropertySymbol? PropMemberSym { get; }

        /// <summary>Member access expression.</summary>
        public string MemberAccess { get; }

        /// <summary>Parent variable name (for nested types).</summary>
        public string? ParentVar { get; }

        /// <summary>Owning model data.</summary>
        public ProtocolTypeData Model { get; }

        /// <summary>Model type symbol.</summary>
        public INamedTypeSymbol ModelSym { get; }

        /// <summary>Type name string.</summary>
        public string TypeStr { get; }

        /// <summary>Immutable member expansion round state (array/enum).</summary>
        public RoundState RoundState { get; }

        /// <summary>Whether this member has a non-empty non-round condition tree.</summary>
        public bool IsConditional { get; }

        /// <summary>Argument string for external member values.</summary>
        public string ExternalMemberValueArgs { get; }

        /// <summary>External member values to apply.</summary>
        public List<(string memberName, string memberValue)> ExternalMemberValues { get; }

        /// <summary>Members to track as nullable after generation.</summary>
        public List<string> MemberNullables { get; }

        /// <summary>Current index id reference (used to generate loop variables).</summary>
        public Ref<int> IndexIdRef { get; }

        /// <summary>Callback used to recursively expand members.</summary>
        public Action<BlockNode, BlockNode, INamedTypeSymbol, IEnumerable<(SerializationExpandContext m, string? parant_var, RoundState roundState)>> ExpandMembersCallback { get; }

        /// <summary>Type transform callback.</summary>
        public Func<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax, ProtocolTypeInfo> TransformCallback { get; }

        public TypeSerializerContext(
            SerializationExpandContext member,
            ITypeSymbol memberTypeSym,
            IFieldSymbol? fieldMemberSym,
            IPropertySymbol? propMemberSym,
            string memberAccess,
            string? parentVar,
            ProtocolTypeData model,
            INamedTypeSymbol modelSym,
            string typeStr,
            RoundState roundState,
            bool isConditional,
            string externalMemberValueArgs,
            List<(string memberName, string memberValue)> externalMemberValues,
            List<string> memberNullables,
            Ref<int> indexIdRef,
            Action<BlockNode, BlockNode, INamedTypeSymbol, IEnumerable<(SerializationExpandContext m, string? parant_var, RoundState roundState)>> expandMembersCallback,
            Func<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax, ProtocolTypeInfo> transformCallback)
        {
            Member = member;
            MemberTypeSym = memberTypeSym;
            FieldMemberSym = fieldMemberSym;
            PropMemberSym = propMemberSym;
            MemberAccess = memberAccess;
            ParentVar = parentVar;
            Model = model;
            ModelSym = modelSym;
            TypeStr = typeStr;
            RoundState = roundState;
            IsConditional = isConditional;
            ExternalMemberValueArgs = externalMemberValueArgs;
            ExternalMemberValues = externalMemberValues;
            MemberNullables = memberNullables;
            IndexIdRef = indexIdRef;
            ExpandMembersCallback = expandMembersCallback;
            TransformCallback = transformCallback;
        }

        /// <summary>
        /// Gets the next unique index id.
        /// </summary>
        public int NextIndexId() => ++IndexIdRef.Value;
    }

    /// <summary>
    /// Simple reference wrapper for passing mutable value types.
    /// </summary>
    public class Ref<T>(T value) where T : struct
    {
        public T Value { get; set; } = value;
    }
}
