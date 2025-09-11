﻿using Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class MonoModExtensions
    {
        [MonoMod.MonoModIgnore]
        public static void MakeMethodVirtual(this TypeDefinition type, params MethodDefinition[] ignores) {
            var methods = type.Methods.Where(m => !m.IsConstructor && !m.IsStatic && m.Name != "cctor" && m.Name != "ctor").ToList();
            methods.AddRange(type.Properties.Select(p => p.SetMethod).Where(m => m != null && !m.IsStatic));
            methods.AddRange(type.Properties.Select(p => p.GetMethod).Where(m => m != null && !m.IsStatic));
            foreach (var method in methods) {
                if (ignores.Contains(method)) continue;

                method.IsVirtual = true;
                method.IsNewSlot = true;
            }
        }

        [MonoMod.MonoModIgnore]
        public static void MakeDirect(this TypeDefinition type, out (MethodDefinition wrapped, MethodDefinition origin)[] wrappedMap, params MethodDefinition[] modifies) {
            List<(MethodDefinition from, MethodDefinition to)> maps = new();

            foreach (var method in modifies.Where(m => !m.IsConstructor && !m.IsStatic && m.DeclaringType.FullName == type.FullName)) {
                if (method.Name != "cctor" && method.Name != "ctor"/* && !method.IsVirtual*/) {
                    //CreateForThisType the new replacement method that will take place of the tail method.
                    //So we must ensure we clone to meet the signatures.
                    var wrapped = new MethodDefinition(method.Name, method.Attributes.Remove(Mono.Cecil.MethodAttributes.Virtual), method.ReturnType);
                    wrapped.IsVirtual = false;
                    wrapped.IsNewSlot = false;
                    var instanceMethod = (method.Attributes & Mono.Cecil.MethodAttributes.Static) == 0;


                    //Clone the parameters for the new method
                    if (method.HasParameters) {
                        foreach (var prm in method.Parameters) {
                            wrapped.Parameters.Add(prm);
                        }
                    }

                    //Rename the existing method, and replace all references to it so that the new 
                    //method receives the calls instead.
                    method.Name += "_Direct";

                    maps.Add((wrapped, method));

                    //Get the il processor instance so we can modify IL
                    var il = wrapped.Body.GetILProcessor();

                    //If the callback expects the instance, emit 'this'
                    if (instanceMethod)
                        il.Emit(OpCodes.Ldarg_0);

                    //If there are parameters, add each of them to the stack for the callback
                    if (wrapped.HasParameters) {
                        for (var i = 0; i < wrapped.Parameters.Count; i++) {
                            //Here we are looking at the callback to see if it wants a reference Parameter.
                            //If it does, and it also expects an instance to be passed, we must move the offset
                            //by one to skip the previous ldarg_0 we added before.
                            //var offset = instanceMethod ? 1 : 0;
                            if (method.Parameters[i /*+ offset*/].ParameterType.IsByReference) {
                                il.Emit(OpCodes.Ldarga, wrapped.Parameters[i]);
                            }
                            else il.Emit(OpCodes.Ldarg, wrapped.Parameters[i]);
                        }
                    }

                    //Execute the callback
                    il.Emit(OpCodes.Call, method);

                    //If the end call has a value, pop it for the time being.
                    //In the case of begin callbacks, we use this value to determine
                    //a cancel.
                    //if (method.ReturnType.Name != method.Module.TypeSystem.Void.Name)
                    //    il.Emit(OpCodes.Pop);

                    il.Emit(OpCodes.Ret);

                    //Place the new method in the declaring type of the method we are cloning
                    method.DeclaringType.Methods.Add(wrapped);
                }
            }

            wrappedMap = maps.ToArray();
        }
        private static readonly ConcurrentDictionary<string, bool> _cache = new ConcurrentDictionary<string, bool>();

        public static bool IsTruelyValueType(this TypeReference type) {
            string cacheKey = GetCacheKey(type);

            if (_cache.TryGetValue(cacheKey, out bool cachedResult))
                return cachedResult;

            if (type.IsByReference || type.IsPointer)
                return CacheAndReturn(cacheKey, false);

            // String is normally behave like a value type
            if (!type.IsValueType && type.FullName != type.Module.TypeSystem.String.FullName)
                return CacheAndReturn(cacheKey, false);

            TypeDefinition? typeDef = type.TryResolve();
            if (typeDef is null)
                return CacheAndReturn(cacheKey, false);

            var genericInstance = type as GenericInstanceType;

            foreach (FieldDefinition field in typeDef.Fields) {
                if (field.IsStatic)
                    continue;

                TypeReference fieldType = field.FieldType;

                if (genericInstance is not null)
                    fieldType = InflateGenericParameters(fieldType, genericInstance);

                if (fieldType.ContainsGenericParameter)
                    return CacheAndReturn(cacheKey, false);

                if (!fieldType.IsValueType)
                    return CacheAndReturn(cacheKey, false);

                // predefined value types
                if (field.FieldType.FullName == type.FullName)
                    return CacheAndReturn(cacheKey, true);

                if (!fieldType.IsTruelyValueType())
                    return CacheAndReturn(cacheKey, false);
            }

            return CacheAndReturn(cacheKey, true);
        }

        private static string GetCacheKey(TypeReference type) {
            return type.FullName;
        }

        private static bool CacheAndReturn(string cacheKey, bool result) {
            _cache.TryAdd(cacheKey, result);
            return result;
        }

        private static TypeReference InflateGenericParameters(TypeReference fieldType, GenericInstanceType genericInstance) {
            if (fieldType.IsGenericParameter) {
                GenericParameter genericParam = (GenericParameter)fieldType;
                if (genericParam.Type == GenericParameterType.Type &&
                    genericParam.Position < genericInstance.GenericArguments.Count)
                    return genericInstance.GenericArguments[genericParam.Position];
                return fieldType;
            }

            if (fieldType is TypeSpecification typeSpec) {
                TypeReference inflatedElement = InflateGenericParameters(typeSpec.ElementType, genericInstance);
                return typeSpec switch {
                    ArrayType array => new ArrayType(inflatedElement, array.Rank),
                    PointerType ptr => new PointerType(inflatedElement),
                    ByReferenceType byRef => new ByReferenceType(inflatedElement),
                    GenericInstanceType generic => InflateGenericInstance(generic, inflatedElement, genericInstance),
                    RequiredModifierType mod => new RequiredModifierType(mod.ModifierType, inflatedElement),
                    _ => typeSpec
                };
            }

            return fieldType;
        }

        private static GenericInstanceType InflateGenericInstance(GenericInstanceType generic, TypeReference inflatedElement, GenericInstanceType context) {
            var newGeneric = new GenericInstanceType(inflatedElement);
            foreach (TypeReference arg in generic.GenericArguments)
                newGeneric.GenericArguments.Add(InflateGenericParameters(arg, context));
            return newGeneric;
        }
        public static string GetDebugName(this ParameterDefinition parameter)
            => string.IsNullOrEmpty(parameter.Name) ? "this" : parameter.Name;
        public static bool IsParameterThis(this ParameterDefinition parameter, MethodDefinition method)
            => string.IsNullOrEmpty(parameter.Name) && method.Body.ThisParameter == parameter;
        public static int IndexWithThis(this ParameterDefinition parameter, MethodDefinition method) {
            var index = method.Parameters.IndexOf(parameter);
            if (method.IsStatic || method.IsConstructor) {
                if (index == -1) {
                    throw new ArgumentException("Parameter not found in method", nameof(parameter));
                }
                return index;
            }
            if (index == -1 && method.Body.ThisParameter != parameter) {
                throw new ArgumentException("Parameter not found in method", nameof(parameter));
            }
            return method.Parameters.IndexOf(parameter) + 1;
        }

        public static string GetIdentifier(this FieldReference field) => field.DeclaringType.FullName + "." + field.Name;


        private static void ResolveGenericParam(MethodReference method, out MethodReference resolved, out TypeReference typeToString) {
            if (method is GenericInstanceMethod genericInstanceMethod) {
                method = genericInstanceMethod.ElementMethod;
            }
            typeToString = method.DeclaringType;
            if (typeToString is GenericInstanceType generic) {
                typeToString = generic.ElementType;
            }

            if (method.HasGenericParameters || (typeToString?.HasGenericParameters ?? false)) {
                Dictionary<IGenericParameterProvider, IGenericParameterProvider> providerMap = [];

                var tmpMethod = new MethodReference(method.Name, method.Module.TypeSystem.Void);

                if (method.HasGenericParameters) {
                    for (int i = 0; i < method.GenericParameters.Count; i++) {
                        tmpMethod.GenericParameters.Add(new GenericParameter(method));
                    }
                    providerMap.Add(method, tmpMethod);
                }

                tmpMethod.DeclaringType = method.DeclaringType;

                if (typeToString is not null && typeToString.HasGenericParameters) {
                    var tmpType = new TypeReference(typeToString.Namespace, typeToString.Name, typeToString.Module, typeToString.Scope) {
                        DeclaringType = typeToString.DeclaringType
                    };

                    for (int i = 0; i < typeToString.GenericParameters.Count; i++) {
                        tmpType.GenericParameters.Add(new GenericParameter(tmpType));
                    }
                    providerMap.Add(typeToString, tmpType);
                    typeToString = tmpMethod.DeclaringType = tmpType;
                }

                var mapOption = new MonoModCommon.Structure.MapOption(providers: providerMap);
                foreach (var param in method.Parameters) {
                    tmpMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, MonoModCommon.Structure.DeepMapTypeReference(param.ParameterType, mapOption)));
                }
                tmpMethod.ReturnType = MonoModCommon.Structure.DeepMapTypeReference(method.ReturnType, mapOption);

                method = tmpMethod;
            }
            resolved = method;
        }
        public static string GetIdentifier(this MethodReference method, bool withTypeName = true) {
            var originalType = method.DeclaringType;
            if (method.DeclaringType is null && withTypeName) {
                throw new ArgumentException("DeclaringType is null", nameof(method));
            }
            ResolveGenericParam(method, out var methodToString, out var typeToString);

            var typeName = withTypeName ? typeToString?.FullName + "." : "";

            // Handle anonymous type constructors, they may have a same overload but different Parameter names
            IEnumerable<string> paramStrs;
            if (originalType is not null
                && originalType.Name.OrdinalStartsWith("<>f__AnonymousType")
                && method.Name == ".ctor"
                && originalType.Resolve().Methods.Count(m => m.IsConstructor && !m.IsStatic) > 1) {

                var innerParamStrs = new List<string>();

                var anonymousCtorDef = method.Resolve();
                for (int i = 0; i < anonymousCtorDef.Parameters.Count; i++) {
                    var paramName = anonymousCtorDef.Parameters[i].Name;
                    var paramType = methodToString.Parameters[i].ParameterType;
                    innerParamStrs.Add(paramName + ":" + paramType.FullName);
                }
                paramStrs = innerParamStrs;
            }
            else {
                paramStrs = methodToString.Parameters.Select(p => p.ParameterType.FullName);
            }

            return typeName + method.Name +
                (methodToString.HasGenericParameters ? "<" + string.Join(",", methodToString.GenericParameters.Select(p => "")) + ">" : "") +
                "(" + string.Join(",", paramStrs) + ")";
        }

        public static string GetSimpleIdentifier(this System.Reflection.MethodBase method, bool withTypeName = true) {
            var type = method.DeclaringType;
            if (type is null && withTypeName) {
                throw new ArgumentException("DeclaringType is null", nameof(method));
            }
            var typeName = withTypeName ? method.DeclaringType!.FullName + "." : "";

            return typeName + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)) + ")";
        }

        public static string GetIdentifier(this MethodReference method, bool withTypeName = true, params TypeDefinition[] ignoreParams) {
            var originalType = method.DeclaringType;
            if (method.DeclaringType is null && withTypeName) {
                throw new ArgumentException("DeclaringType is null", nameof(method));
            }
            ResolveGenericParam(method, out var methodToString, out var typeToString);

            var typeName = withTypeName ? typeToString?.FullName + "." : "";

            HashSet<string> ignoreNames = [.. ignoreParams.Select(p => p.FullName)];
            List<string> paramStrs = [];

            // Handle anonymous type constructors, they may have a same overload but different Parameter names
            if (originalType is not null
                && originalType.Name.OrdinalStartsWith("<>f__AnonymousType")
                && method.Name == ".ctor"
                && originalType.Resolve().Methods.Count(m => m.IsConstructor && !m.IsStatic) > 1) {

                var anonymousCtorDef = method.Resolve();
                for (int i = 0; i < anonymousCtorDef.Parameters.Count; i++) {
                    var paramName = anonymousCtorDef.Parameters[i].Name;
                    var paramType = methodToString.Parameters[i].ParameterType;
                    if (ignoreNames.Contains(paramType.FullName)) {
                        continue;
                    }
                    paramStrs.Add(paramName + ":" + paramType.FullName);
                }
            }
            else {
                for (int i = 0; i < methodToString.Parameters.Count; i++) {
                    var param = methodToString.Parameters[i];
                    if (ignoreNames.Contains(param.ParameterType.FullName)) {
                        continue;
                    }
                    paramStrs.Add(param.ParameterType.FullName);
                }
            }

            return typeName + method.Name +
                (methodToString.HasGenericParameters ? "<" + string.Join(",", methodToString.GenericParameters.Select(p => "")) + ">" : "") +
                "(" + string.Join(",", paramStrs) + ")";
        }
        public static string GetIdentifier(this MethodReference method, bool withTypeName, Dictionary<string, string> typeNameMap, HashSet<int>? forceRefParams = null) {
            var originalType = method.DeclaringType;
            if (method.DeclaringType is null && withTypeName) {
                throw new ArgumentException("DeclaringType is null", nameof(method));
            }

            typeNameMap ??= [];
            forceRefParams ??= [];

            ResolveGenericParam(method, out var methodToString, out var typeToString);

            string typeName = "";

            if (withTypeName) {
                if (typeNameMap.TryGetValue(method.DeclaringType!.FullName, out var alias)) {
                    typeName = alias;
                }
                else if (typeToString is not null) {
                    typeName = typeToString.FullName;
                }
                else {
                    typeName = method.DeclaringType.FullName;
                }
                typeName += ".";
            }

            List<string> paramStrs = [];

            // Handle anonymous type constructors, they may have a same overload but different Parameter names
            if (originalType is not null
                && originalType.Name.OrdinalStartsWith("<>f__AnonymousType")
                && method.Name == ".ctor"
                && originalType.Resolve().Methods.Count(m => m.IsConstructor && !m.IsStatic) > 1) {

                var anonymousCtorDef = method.Resolve();
                for (int i = 0; i < anonymousCtorDef.Parameters.Count; i++) {
                    var paramName = anonymousCtorDef.Parameters[i].Name;
                    var paramType = methodToString.Parameters[i].ParameterType;
                    paramStrs.Add(paramName + ":" + (typeNameMap.TryGetValue(paramType.FullName, out var alise) ? alise : paramType.FullName));
                }
            }
            else {
                for (int i = 0; i < methodToString.Parameters.Count; i++) {

                    var paramType = methodToString.Parameters[i].ParameterType;

                    if (typeNameMap.TryGetValue(paramType.FullName, out var alias)) {
                        if (forceRefParams.Contains(i)) {
                            paramStrs.Add(alias + "&");
                        }
                        else {
                            paramStrs.Add(alias);
                        }
                    }
                    else if (paramType is ByReferenceType referenceType && typeNameMap.TryGetValue(referenceType.ElementType.FullName, out var refAlias)) {
                        paramStrs.Add(refAlias + "&");
                    }
                    else {
                        paramStrs.Add(paramType.FullName);
                    }
                }
            }

            return typeName + method.Name +
                (methodToString.HasGenericParameters ? "<" + string.Join(",", methodToString.GenericParameters.Select(p => "")) + ">" : "") +
                "(" + string.Join(",", paramStrs) + ")";
        }
        public static string GetDebugName(this MethodReference method, bool fullTypeName = false) {
            return (fullTypeName ? method.DeclaringType.FullName : method.DeclaringType.Name) + "." + method.Name +
                (method.HasGenericParameters ? "<" + string.Join(",", method.GenericParameters.Select(p => "")) + ">" : "") +
                "(" + string.Join(",", method.Parameters.Select(p => p.ParameterType.Name)) + ")";
        }
        public static bool IsDelegate(this TypeReference type) =>
            type.Name == nameof(MulticastDelegate) ||
            type?.Resolve()?.BaseType?.Name == nameof(MulticastDelegate);

        public static TypeDefinition GetRootDeclaringType(this TypeDefinition type) {
            while (type.DeclaringType is not null) {
                type = type.DeclaringType;
            }
            return type;
        }

        public static TypeDefinition? TryResolve(this TypeReference type) {
            try {
                return type.Resolve();
            }
            catch {
                return null;
            }
        }
        public static MethodDefinition? TryResolve(this MethodReference method) {
            try {
                return method.Resolve();
            }
            catch {
                return null;
            }
        }
        public static FieldDefinition? TryResolve(this FieldReference field) {
            try {
                return field.Resolve();
            }
            catch {
                return null;
            }
        }
        public static FieldDefinition GetField(this TypeDefinition type, string name) {
            return type.Fields.Single((FieldDefinition x) => x.Name == name);
        }

        public static MethodDefinition GetMethod(this TypeDefinition type, string name) {
            return type.Methods.Single((MethodDefinition x) => x.Name == name);
        }

        public static EventDefinition GetEvent(this TypeDefinition type, string name) {
            return type.Events.Single((EventDefinition x) => x.Name == name);
        }

        public static PropertyDefinition GetProperty(this TypeDefinition type, string name) {
            return type.Properties.Single((PropertyDefinition x) => x.Name == name);
        }

        /// <summary>
        /// Inserts instructions before a target instruction, adjusts jump targets and exception blocks
        /// </summary>
        /// <param name="iLProcessor"></param>
        /// <param name="jumpSites"></param>
        /// <param name="target"></param>
        /// <param name="instructions"></param>
        public static void InsertBeforeAndAdjustTargets(this ILProcessor iLProcessor, Dictionary<Instruction, List<Instruction>> jumpSites, Instruction target, params IEnumerable<Instruction> instructions) {
            Instruction? first = instructions.FirstOrDefault();
            if (first is null) {
                return;
            }

            foreach (var instruction in instructions) {
                iLProcessor.InsertBefore(target, instruction);
            }

            if (jumpSites.TryGetValue(target, out var sites)) {
                foreach (var jumpSite in sites) {
                    if (jumpSite.Operand is ILLabel label) {
                        label.Target = first;
                    }
                    else if (jumpSite.Operand is Instruction) {
                        jumpSite.Operand = first;
                    }
                    else {
                        var jumpTargets = (Instruction[])jumpSite.Operand;
                        for (int i = 0; i < jumpTargets.Length; i++) {
                            if (jumpTargets[i] == target) {
                                jumpTargets[i] = first;
                            }
                        }
                    }
                }
            }

            if (iLProcessor.Body.HasExceptionHandlers) {
                foreach (var exceptionHandler in iLProcessor.Body.ExceptionHandlers) {
                    if (exceptionHandler.TryStart == target) {
                        exceptionHandler.TryStart = first;
                    }
                    else if (exceptionHandler.HandlerStart == target) {
                        exceptionHandler.HandlerStart = first;
                    }
                    else if (exceptionHandler.FilterStart == target) {
                        exceptionHandler.FilterStart = first;
                    }
                }
            }
        }

        public static Instruction Clone(this Instruction instruction) {
            var clone = Instruction.Create(OpCodes.Nop);
            clone.OpCode = instruction.OpCode;
            clone.Operand = instruction.Operand;
            return clone;
        }

        /// <summary>
        /// Seamlessly inserts a sequence of instructions before the target instruction by modifying the target's content in-place.
        /// <para>
        /// - The original target instruction's opcode and operand will be REPLACED with the FIRST instruction of <paramref name="instructions"/>.<br/>
        /// - The remaining instructions are inserted after the modified target instruction.<br/>
        /// - The original target instruction's content is appended as a NEW instruction at the end of the inserted sequence.<br/>
        /// </para>
        /// All existing branches and exception blocks pointing to the original target will now implicitly point to the new inserted sequence's head, 
        /// while execution flow remains logically equivalent.
        /// </summary>
        /// <param name="iLProcessor">The ILProcessor context for IL manipulation.</param>
        /// <param name="target">[IN/OUT] Reference to the target instruction. After insertion, this reference will point to the NEWLY CREATED instruction containing the original target's content.</param>
        /// <param name="instructions">The instructions to insert. Must contain at least one instruction.</param>
        /// <remarks>
        /// WARNING: The original <paramref name="target"/> reference becomes invalid after this operation. 
        /// Use the updated reference via the 'ref' Parameter for subsequent operations.
        /// </remarks>
        public static void InsertBeforeSeamlessly(this ILProcessor iLProcessor, ref Instruction target, params IEnumerable<Instruction> instructions) {
            InsertBeforeSeamlessly(iLProcessor, ref target, out _, instructions);
        }
        /// <summary>
        /// Seamlessly inserts a sequence of instructions before the target instruction and returns the first inserted instruction.
        /// <para>
        /// - Identical behavior to <see cref="InsertBeforeSeamlessly(ILProcessor, ref Instruction, IEnumerable{Instruction})"/>.<br/>
        /// - Additionally provides the first instruction of the inserted sequence via <paramref name="first"/> Parameter.
        /// </para>
        /// </summary>
        /// <param name="ilProcessor">The ILProcessor context for IL manipulation.</param>
        /// <param name="target">[IN/OUT] Reference to the target instruction. After insertion, this reference will point to the NEWLY CREATED instruction containing the original target's content.</param>
        /// <param name="first">[OUT] The first instruction of the inserted sequence (i.e., the instruction that replaced the original target's content).</param>
        /// <param name="instructions">The instructions to insert. Must contain at least one instruction.</param>
        public static void InsertBeforeSeamlessly(this ILProcessor ilProcessor, ref Instruction target, out Instruction first, params IEnumerable<Instruction> instructions) {
            first = instructions.FirstOrDefault() ?? throw new ArgumentNullException(nameof(instructions), "At least one instruction is required.");

            // --- Step 1: Backup Original Target Metadata ---
            var originalOpCode = target.OpCode;
            var originalOperand = target.Operand;

            // --- Step 2: Replace Target with First Inserted Instruction ---
            var firstInserted = first;
            target.OpCode = firstInserted.OpCode;
            target.Operand = firstInserted.Operand;
            first = target;  // Out Parameter points to modified target

            // --- Step 3: Insert Remaining Instructions After Modified Target ---
            Instruction current = target;
            foreach (var instr in instructions.Skip(1)) {
                ilProcessor.InsertAfter(current, instr);
                current = instr;
            }

            // --- Step 4: Append Original Target as New Instruction ---
            var restoredOriginal = Instruction.Create(OpCodes.Nop);
            restoredOriginal.OpCode = originalOpCode;
            restoredOriginal.Operand = originalOperand;
            ilProcessor.InsertAfter(current, restoredOriginal);

            // --- Step 5: Update Target Reference ---
            target = restoredOriginal;  // Caller's ref now points to the restored original
        }
        /// <summary>
        /// Seamlessly removes a specified instruction from a method body while preserving branch targets and exception handler boundaries.
        /// Converts the instruction to Nop if it's referenced elsewhere, otherwise removes it completely.
        /// </summary>
        /// <param name="body">The method body containing the instruction</param>
        /// <param name="jumpSites">Dictionary tracing branch targets (key) and their jump sources (value)</param>
        /// <param name="instruction">The target instruction to remove</param>
        /// <param name="removeFrom">Optional collection to remove from (defaults to body.Instructions)</param>
        public static void RemoveInstructionSeamlessly(this MethodBody body,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            Instruction instruction,
            ICollection<Instruction>? removeFrom = null) {
            removeFrom ??= body.Instructions;

            // If instruction is a branch target, convert to Nop but keep position
            // to maintain jump offsets while neutralizing its operation
            if (jumpSites.ContainsKey(instruction)) {
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;  // Clear any associated data
                return;
            }

            // Check if instruction is part of any exception handler boundaries
            bool isExceptionBoundary = false;
            if (body.HasExceptionHandlers) {
                foreach (var handler in body.ExceptionHandlers) {
                    // Validate against all handler boundary markers
                    if (handler.TryStart == instruction ||
                        handler.TryEnd == instruction ||
                        handler.HandlerStart == instruction ||
                        handler.HandlerEnd == instruction ||
                        handler.FilterStart == instruction) {
                        isExceptionBoundary = true;
                        break; // Early exit after finding first boundary reference
                    }
                }
            }

            // If part of exception structure, neutralize but preserve position
            if (isExceptionBoundary) {
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;  // Clear exception-related data
                return;
            }

            // Safe to physically remove if not referenced by control flow or exceptions
            removeFrom.Remove(instruction);
        }
    }
}
