using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Text;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static partial class MonoModExtensions
    {
        // Controls whether malformed generic metadata in identifier formatting should throw or fallback.
        public static bool ThrowOnGetIdentifierMetadataMismatch { get; set; } = true;

        public static string GetIdentifier(this MethodReference method, bool withDeclaring = true)
            => GetIdentifierCore(method, withDeclaring, null, null, null);

        public static string GetIdentifier(this MethodReference method, bool withDeclaring = true, params TypeDefinition[] ignoreParams)
            => GetIdentifierCore(method, withDeclaring, null, null, ignoreParams);

        public static string GetIdentifier(this MethodReference method, bool withDeclaring, Dictionary<string, string> typeNameMap, HashSet<int>? makeByRefIfNot = null)
            => GetIdentifierCore(method, withDeclaring, typeNameMap, makeByRefIfNot, null);

        private static string GetIdentifierCore(
            MethodReference method,
            bool withDeclaring,
            Dictionary<string, string>? typeNameMap,
            HashSet<int>? makeByRefIfNot,
            TypeDefinition[]? ignoreParams) {

            ArgumentNullException.ThrowIfNull(method);

            typeNameMap ??= [];
            makeByRefIfNot ??= [];

            var methodToFormat = NormalizeMethodReference(method);
            if (withDeclaring && methodToFormat.DeclaringType is null) {
                throw new ArgumentException("DeclaringType is null", nameof(method));
            }

            HashSet<string>? ignoredTypeNames = null;
            if (ignoreParams is { Length: > 0 }) {
                ignoredTypeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var ignoreParam in ignoreParams) {
                    ignoredTypeNames.Add(ignoreParam.FullName);
                }
            }

            var identifierBuilder = new StringBuilder();

            if (withDeclaring) {
                identifierBuilder.Append(GetTypeRestrictionName(methodToFormat.DeclaringType!, typeNameMap));
                identifierBuilder.Append("::");
            }

            identifierBuilder.Append(methodToFormat.Name);
            var methodGenericParameterCount = methodToFormat.GenericParameters.Count;
            if (methodGenericParameterCount > 0) {
                identifierBuilder.Append('`');
                identifierBuilder.Append(methodGenericParameterCount);
            }

            var useAnonymousCtorNamedParameters = ShouldUseAnonymousCtorNamedParameters(methodToFormat, out var anonymousCtorDef);

            identifierBuilder.Append('(');
            var appendedParameterCount = 0;
            for (var i = 0; i < methodToFormat.Parameters.Count; i++) {
                var parameterType = methodToFormat.Parameters[i].ParameterType;
                if (ShouldIgnoreParameter(parameterType, ignoredTypeNames)) {
                    continue;
                }

                if (appendedParameterCount > 0) {
                    identifierBuilder.Append(',');
                }

                var parameterTypeName = GetParameterTypeName(parameterType, typeNameMap);

                if (makeByRefIfNot.Contains(i) && parameterType is not ByReferenceType) {
                    parameterTypeName += "&";
                }

                if (useAnonymousCtorNamedParameters) {
                    var parameterName = GetAnonymousCtorParameterName(methodToFormat, anonymousCtorDef, i);
                    identifierBuilder.Append(parameterName);
                    identifierBuilder.Append(':');
                }

                identifierBuilder.Append(parameterTypeName);
                appendedParameterCount++;
            }

            identifierBuilder.Append(')');
            return identifierBuilder.ToString();
        }

        private static MethodReference NormalizeMethodReference(MethodReference method) {
            MethodReference candidate = method;

            if (candidate is GenericInstanceMethod genericInstanceMethod) {
                candidate = genericInstanceMethod.ElementMethod;
            }

            if (candidate.DeclaringType is GenericInstanceType && candidate.Resolve() is MethodDefinition resolvedMethod) {
                candidate = resolvedMethod;
            }

            return candidate;
        }

        private static bool ShouldIgnoreParameter(TypeReference parameterType, HashSet<string>? ignoredTypeNames) {
            if (ignoredTypeNames is null || ignoredTypeNames.Count == 0) {
                return false;
            }

            var checkType = parameterType is ByReferenceType byReferenceType ? byReferenceType.ElementType : parameterType;
            checkType = checkType.GetElementType();
            return ignoredTypeNames.Contains(checkType.FullName);
        }

        private static string GetTypeRestrictionName(TypeReference declaringType, Dictionary<string, string> typeNameMap) {
            var typeToFormat = declaringType is GenericInstanceType genericInstanceType
                ? genericInstanceType.ElementType
                : declaringType.GetElementType();

            if (typeNameMap.TryGetValue(typeToFormat.FullName, out var mappedName)) {
                return mappedName;
            }

            var chain = BuildDeclaringTypeChain(typeToFormat);
            var builder = new StringBuilder();

            for (var i = 0; i < chain.Count; i++) {
                var current = chain[i];

                if (i == 0) {
                    if (!string.IsNullOrEmpty(current.Namespace)) {
                        builder.Append(current.Namespace);
                        builder.Append('.');
                    }
                }
                else {
                    builder.Append('/');
                }

                builder.Append(StripGenericArity(current.Name));

                var arity = GetGenericArity(current);
                if (arity > 0) {
                    builder.Append('`');
                    builder.Append(arity);
                }
            }

            return builder.ToString();
        }

        private static string GetParameterTypeName(TypeReference type, Dictionary<string, string> typeNameMap) {
            if (type is ByReferenceType byReferenceType) {
                return GetParameterTypeName(byReferenceType.ElementType, typeNameMap) + "&";
            }

            if (type is PointerType pointerType) {
                return GetParameterTypeName(pointerType.ElementType, typeNameMap) + "*";
            }

            if (type is ArrayType arrayType) {
                return GetParameterTypeName(arrayType.ElementType, typeNameMap) + GetArraySuffix(arrayType);
            }

            if (type is PinnedType pinnedType) {
                return GetParameterTypeName(pinnedType.ElementType, typeNameMap);
            }

            if (type is OptionalModifierType optionalModifierType) {
                return GetParameterTypeName(optionalModifierType.ElementType, typeNameMap);
            }

            if (type is RequiredModifierType requiredModifierType) {
                return GetParameterTypeName(requiredModifierType.ElementType, typeNameMap);
            }

            if (type is SentinelType sentinelType) {
                return GetParameterTypeName(sentinelType.ElementType, typeNameMap);
            }

            if (type is GenericParameter genericParameter) {
                return genericParameter.Type == GenericParameterType.Method
                    ? $"!!{genericParameter.Position}"
                    : $"!{genericParameter.Position}";
            }

            if (typeNameMap.TryGetValue(type.FullName, out var exactMappedTypeName)) {
                return NormalizeTypeNameForParameter(exactMappedTypeName);
            }

            if (type is GenericInstanceType genericInstanceType) {
                return FormatGenericInstanceParameterType(genericInstanceType, typeNameMap);
            }

            var elementType = type.GetElementType();
            if (typeNameMap.TryGetValue(elementType.FullName, out var mappedElementTypeName)) {
                return NormalizeTypeNameForParameter(mappedElementTypeName);
            }

            return FormatNamedTypeForParameter(type, typeNameMap);
        }

        private static string FormatGenericInstanceParameterType(GenericInstanceType genericInstanceType, Dictionary<string, string> typeNameMap) {
            var elementType = genericInstanceType.ElementType.GetElementType();

            if (typeNameMap.TryGetValue(elementType.FullName, out var mappedElementTypeName)) {
                return AppendGenericArgumentsToMappedType(
                    NormalizeTypeNameForParameter(mappedElementTypeName),
                    genericInstanceType.GenericArguments,
                    typeNameMap);
            }

            var chain = BuildDeclaringTypeChain(elementType);
            var builder = new StringBuilder();
            var nextArgIndex = 0;

            for (var i = 0; i < chain.Count; i++) {
                var part = chain[i];

                if (i == 0) {
                    if (!string.IsNullOrEmpty(part.Namespace)) {
                        builder.Append(part.Namespace);
                        builder.Append('.');
                    }
                }
                else {
                    builder.Append('.');
                }

                builder.Append(StripGenericArity(part.Name));

                var partArity = GetGenericArity(part);
                if (partArity <= 0) {
                    continue;
                }

                builder.Append('<');

                for (var argOffset = 0; argOffset < partArity; argOffset++) {
                    if (argOffset > 0) {
                        builder.Append(',');
                    }

                    if (TryConsumeGenericArgument(
                        genericInstanceType,
                        part,
                        argOffset,
                        ref nextArgIndex,
                        out var consumedArgumentType)) {
                        builder.Append(GetParameterTypeName(consumedArgumentType!, typeNameMap));
                    }
                    else {
                        HandleMetadataMismatch(
                            $"Missing generic argument while formatting '{genericInstanceType.FullName}'. " +
                            $"Expected slot index {argOffset} for part '{part.FullName}', " +
                            $"explicit arguments consumed {nextArgIndex}/{genericInstanceType.GenericArguments.Count}.");
                        builder.Append('!');
                        builder.Append(argOffset);
                    }
                }

                builder.Append('>');
            }

            if (nextArgIndex < genericInstanceType.GenericArguments.Count) {
                HandleMetadataMismatch(
                    $"Redundant generic arguments while formatting '{genericInstanceType.FullName}'. " +
                    $"Consumed {nextArgIndex}, actual {genericInstanceType.GenericArguments.Count}.");
                builder.Append('<');
                for (var i = nextArgIndex; i < genericInstanceType.GenericArguments.Count; i++) {
                    if (i > nextArgIndex) {
                        builder.Append(',');
                    }

                    builder.Append(GetParameterTypeName(genericInstanceType.GenericArguments[i], typeNameMap));
                }
                builder.Append('>');
            }

            return builder.ToString();
        }

        private static bool TryConsumeGenericArgument(
            GenericInstanceType genericInstanceType,
            TypeReference typePart,
            int slotIndex,
            ref int nextExplicitArgumentIndex,
            out TypeReference? argumentType) {

            if (nextExplicitArgumentIndex < genericInstanceType.GenericArguments.Count) {
                argumentType = genericInstanceType.GenericArguments[nextExplicitArgumentIndex];
                nextExplicitArgumentIndex++;
                return true;
            }

            if (slotIndex < typePart.GenericParameters.Count) {
                argumentType = typePart.GenericParameters[slotIndex];
                return true;
            }

            argumentType = null;
            return false;
        }

        private static string AppendGenericArgumentsToMappedType(
            string mappedTypeName,
            ICollection<TypeReference> genericArguments,
            Dictionary<string, string> typeNameMap) {

            if (genericArguments.Count == 0) {
                return mappedTypeName;
            }

            var builder = new StringBuilder(mappedTypeName);
            builder.Append('<');

            var index = 0;
            foreach (var argument in genericArguments) {
                if (index > 0) {
                    builder.Append(',');
                }

                builder.Append(GetParameterTypeName(argument, typeNameMap));
                index++;
            }

            builder.Append('>');
            return builder.ToString();
        }

        private static string FormatNamedTypeForParameter(TypeReference type, Dictionary<string, string> typeNameMap) {
            var elementType = type.GetElementType();
            var simpleName = StripGenericArity(elementType.Name);

            if (elementType.DeclaringType is not null) {
                return GetParameterTypeName(elementType.DeclaringType, typeNameMap) + "." + simpleName;
            }

            if (typeNameMap.TryGetValue(elementType.FullName, out var mappedElementTypeName)) {
                return NormalizeTypeNameForParameter(mappedElementTypeName);
            }

            if (!string.IsNullOrEmpty(elementType.Namespace)) {
                return elementType.Namespace + "." + simpleName;
            }

            return simpleName;
        }

        private static bool ShouldUseAnonymousCtorNamedParameters(MethodReference method, out MethodDefinition? anonymousCtorDef) {
            anonymousCtorDef = null;

            if (method.Name != ".ctor") {
                return false;
            }

            var declaringType = method.DeclaringType;
            if (declaringType is null || !declaringType.Name.OrdinalStartsWith("<>f__AnonymousType")) {
                return false;
            }

            var declaringTypeDef = declaringType.Resolve();
            if (declaringTypeDef is null) {
                HandleMetadataMismatch($"Failed to resolve anonymous declaring type '{declaringType.FullName}'.");
                return false;
            }

            var instanceCtorCount = 0;
            foreach (var maybeCtor in declaringTypeDef.Methods) {
                if (maybeCtor.IsConstructor && !maybeCtor.IsStatic) {
                    instanceCtorCount++;
                    if (instanceCtorCount > 1) {
                        break;
                    }
                }
            }

            if (instanceCtorCount <= 1) {
                return false;
            }

            // MethodReference often loses constructor parameter names; resolve definition for stable names.
            anonymousCtorDef = method.Resolve();
            if (anonymousCtorDef is null) {
                HandleMetadataMismatch($"Failed to resolve anonymous constructor '{declaringType.FullName}::{method.Name}'.");
            }

            return true;
        }

        private static string GetAnonymousCtorParameterName(MethodReference method, MethodDefinition? anonymousCtorDef, int parameterIndex) {
            var parameterName = method.Parameters[parameterIndex].Name;
            if (string.IsNullOrEmpty(parameterName)
                && anonymousCtorDef is not null
                && parameterIndex < anonymousCtorDef.Parameters.Count) {
                parameterName = anonymousCtorDef.Parameters[parameterIndex].Name;
            }

            if (!string.IsNullOrEmpty(parameterName)) {
                return parameterName;
            }

            HandleMetadataMismatch(
                $"Anonymous constructor parameter name is missing: '{method.DeclaringType?.FullName}::{method.Name}' index {parameterIndex}.");
            return "arg" + parameterIndex;
        }

        private static string NormalizeTypeNameForParameter(string typeName) {
            if (string.IsNullOrEmpty(typeName)) {
                return typeName;
            }

            var normalized = typeName.Replace('/', '.');
            var builder = new StringBuilder(normalized.Length);

            for (var i = 0; i < normalized.Length; i++) {
                var current = normalized[i];
                if (current != '`') {
                    builder.Append(current);
                    continue;
                }

                var digitStart = i + 1;
                var digitLength = 0;
                while (digitStart + digitLength < normalized.Length && char.IsDigit(normalized[digitStart + digitLength])) {
                    digitLength++;
                }

                if (digitLength == 0) {
                    builder.Append(current);
                    continue;
                }

                var nextIndex = digitStart + digitLength;
                if (nextIndex == normalized.Length || IsTypeNameDelimiter(normalized[nextIndex])) {
                    i = nextIndex - 1;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static string GetArraySuffix(ArrayType arrayType) {
            if (arrayType.Rank <= 1) {
                return "[]";
            }

            return "[" + new string(',', arrayType.Rank - 1) + "]";
        }

        private static List<TypeReference> BuildDeclaringTypeChain(TypeReference type) {
            var chain = new List<TypeReference>();

            for (TypeReference? current = type; current is not null; current = current.DeclaringType) {
                var unwrapped = current is GenericInstanceType genericInstanceType
                    ? genericInstanceType.ElementType
                    : current.GetElementType();

                chain.Add(unwrapped);
            }

            chain.Reverse();
            return chain;
        }

        private static int GetGenericArity(TypeReference type) {
            if (TryParseTrailingGenericArity(type.Name, out _, out var parsedArity)) {
                return parsedArity;
            }

            return type.HasGenericParameters ? type.GenericParameters.Count : 0;
        }

        private static string StripGenericArity(string typeName) {
            return TryParseTrailingGenericArity(typeName, out var tickIndex, out _)
                ? typeName[..tickIndex]
                : typeName;
        }

        private static bool TryParseTrailingGenericArity(string typeName, out int tickIndex, out int arity) {
            tickIndex = typeName.LastIndexOf('`');
            arity = 0;

            if (tickIndex < 0 || tickIndex + 1 >= typeName.Length) {
                return false;
            }

            var span = typeName.AsSpan(tickIndex + 1);
            if (!int.TryParse(span, out arity)) {
                return false;
            }

            return true;
        }

        private static bool IsTypeNameDelimiter(char c) {
            return c is '.' or '<' or '>' or ',' or '[' or ']' or '&' or '*' or '+';
        }

        private static void HandleMetadataMismatch(string message) {
            if (ThrowOnGetIdentifierMetadataMismatch) {
                throw new InvalidOperationException(message);
            }
        }
    }
}
