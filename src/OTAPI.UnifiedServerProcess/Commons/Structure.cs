using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Commons {
    public partial class MonoModCommon {
        public static class Structure {
            public static TypeReference CreateTypeReference(TypeDefinition type, ModuleDefinition module) {
                var result = new TypeReference(type.Namespace, type.Name, module, type.Scope) { 
                    IsValueType = type.IsValueType,
                };
                if (result.HasGenericParameters) {
                    foreach (var param in type.GenericParameters) { 
                        result.GenericParameters.Add(new GenericParameter(param.Name, result));
                    }
                }
                if (type.DeclaringType is not null) {
                    result.DeclaringType = CreateTypeReference(type.DeclaringType, module);
                }
                return result;
            }
            static string GenerateKeyForGenericParameter(GenericParameter param) {
                if (param.DeclaringMethod is not null) {
                    return param.DeclaringMethod.GetIdentifier() + ":" + param.Position;
                }
                else if (param.DeclaringType is not null) {
                    return param.DeclaringType.Resolve().FullName + ":" + param.Position;
                }
                else {
                    throw new InvalidOperationException("Unknown generic parameter");
                }
            }
            public readonly struct MapOption {
                public MapOption() {
                    this.MethodReplaceMap = [];
                    this.TypeReplaceMap = [];
                    this.GenericProvider = [];
                    this.GenericParameterMap = [];
                }
                public MapOption(
                    Dictionary<TypeDefinition, TypeDefinition>? typeReplace = null,
                    Dictionary<MethodDefinition, MethodDefinition>? methodReplace = null,
                    Dictionary<IGenericParameterProvider, IGenericParameterProvider>? providers = null,
                    Dictionary<GenericParameter, TypeReference>? genericParameterMap = null) {
                    this.MethodReplaceMap = methodReplace ?? [];
                    this.TypeReplaceMap = typeReplace ?? [];
                    this.GenericProvider = providers ?? [];
                    this.GenericParameterMap = genericParameterMap?.ToDictionary(kv => GenerateKeyForGenericParameter(kv.Key), kv => kv.Value) ?? [];
                }
                public static MapOption Create(
                    (TypeDefinition from, TypeDefinition to)[]? replaceType = null,
                    (MethodDefinition from, MethodDefinition to)[]? replaceMethod = null,
                    (IGenericParameterProvider provideFrom, IGenericParameterProvider provideTo)[]? providers = null,
                    (GenericParameter paramFrom, TypeReference typeTo)[]? genericParameterMap = null) {
                    return new MapOption(
                        replaceType?.ToDictionary(x => x.from, x => x.to) ?? [],
                        replaceMethod?.ToDictionary(x => x.from, x => x.to) ?? [],
                        providers?.ToDictionary(x => x.provideFrom, x => x.provideTo) ?? [],
                        genericParameterMap?.ToDictionary(x => x.paramFrom, x => x.typeTo) ?? []);
                }
                public static MapOption CreateGenericProviderMap(
                    (IGenericParameterProvider provideFrom, IGenericParameterProvider provideTo)[]? providers = null) {
                    return new MapOption(
                        [],
                        [],
                        providers?.ToDictionary(x => x.provideFrom, x => x.provideTo) ?? []);
                }
                public readonly Dictionary<TypeDefinition, TypeDefinition> TypeReplaceMap;
                public readonly Dictionary<MethodDefinition, MethodDefinition> MethodReplaceMap;
                public readonly Dictionary<IGenericParameterProvider, IGenericParameterProvider> GenericProvider;
                public readonly Dictionary<string, TypeReference> GenericParameterMap;
            }
            public static GenericInstanceType DeepMapGenericInstanceType(GenericInstanceType instance, MapOption option) {
                var pattern = instance.ElementType;
                if (option.TypeReplaceMap.TryGetValue(pattern.Resolve(), out var mappedPattern)) {
                    pattern = mappedPattern;
                }

                var result = new GenericInstanceType(pattern);
                foreach (var arg in instance.GenericArguments) {
                    if (arg is GenericInstanceType nestedGeneric) {
                        result.GenericArguments.Add(DeepMapGenericInstanceType(nestedGeneric, option));
                    }
                    else if (arg is GenericParameter genericParam) {
                        result.GenericArguments.Add(DeepMapGenericParameter(genericParam, option));
                    }
                    else if (arg is TypeReference typeRef) {
                        result.GenericArguments.Add(DeepMapTypeReference(typeRef, option));
                    }
                }
                return result;
            }
            public static TypeReference DeepMapGenericParameter(GenericParameter param, MapOption option) {
                if (option.GenericParameterMap.Count > 0 && option.GenericParameterMap.TryGetValue(GenerateKeyForGenericParameter(param), out var mappedParam)) {
                    return mappedParam;
                }
                GenericParameter copiedGenericParam;
                if (param.DeclaringMethod is not null) {
                    var elementMethodRef = param.DeclaringMethod.GetElementMethod();
                    var elementMethodDef = elementMethodRef.Resolve();

                    if (elementMethodDef is not null && option.MethodReplaceMap.TryGetValue(elementMethodDef, out var methodReplace)) {
                        elementMethodRef = methodReplace;
                    }
                    else if (option.GenericProvider.TryGetValue(elementMethodRef, out var genericProvider) && genericProvider is MethodReference genericProviderMethod) {
                        elementMethodRef = genericProviderMethod;
                    }
                    copiedGenericParam = elementMethodRef.GenericParameters[param.Position];
                }
                else if (param.DeclaringType is not null) {
                    var elementTypeRef = param.DeclaringType.GetElementType();
                    var elementTypeDef = elementTypeRef.Resolve();
                    if (elementTypeDef is not null && option.TypeReplaceMap.TryGetValue(elementTypeDef, out var typeReplace)) {
                        elementTypeRef = typeReplace;
                    }
                    else if (option.GenericProvider.TryGetValue(elementTypeRef, out var genericProvider) && genericProvider is TypeReference genericProviderType) {
                        elementTypeRef = genericProviderType;
                    }
                    copiedGenericParam = elementTypeRef.GenericParameters[param.Position];
                }
                else {
                    throw new InvalidOperationException("Unknown generic parameter");
                }
                return copiedGenericParam;
            }
            public static GenericInstanceMethod DeepMapGenericInstanceMethod(GenericInstanceMethod instance, MapOption option) {
                var pattern = instance.ElementMethod;
                if (option.MethodReplaceMap.TryGetValue(pattern.Resolve(), out var mappedPattern)) {
                    pattern = mappedPattern;
                }

                var result = new GenericInstanceMethod(pattern);
                foreach (var arg in instance.GenericArguments) {
                    if (arg is GenericInstanceType nestedGeneric) {
                        result.GenericArguments.Add(DeepMapGenericInstanceType(nestedGeneric, option));
                    }
                    else if (arg is GenericParameter genericParam) {
                        result.GenericArguments.Add(DeepMapGenericParameter(genericParam, option));
                    }
                    else if (arg is TypeReference typeRef) {
                        result.GenericArguments.Add(DeepMapTypeReference(typeRef, option));
                    }
                }
                return result;
            }
            public static TypeReference DeepMapTypeReference(TypeReference type, MapOption option) {
                if (type is GenericInstanceType generic) {
                    return DeepMapGenericInstanceType(generic, option);
                }
                else if (type is GenericParameter genericParam) {
                    return DeepMapGenericParameter(genericParam, option);
                }
                else if (type is ArrayType array) {
                    return new ArrayType(DeepMapTypeReference(array.ElementType, option), array.Rank);
                }
                else if (type is ByReferenceType byRef) {
                    return new ByReferenceType(DeepMapTypeReference(byRef.ElementType, option));
                }
                else if (type is PointerType ptr) {
                    return new PointerType(DeepMapTypeReference(ptr.ElementType, option));
                }
                else if (type is FunctionPointerType fnptr) {
                    return new PointerType(DeepMapTypeReference(fnptr.ElementType, option));
                }
                else if (type is RequiredModifierType required) {
                    return new RequiredModifierType(required.ModifierType, DeepMapTypeReference(required.ElementType, option));
                }
                var typeDef = type.Resolve();
                if (typeDef is not null && option.TypeReplaceMap.TryGetValue(typeDef, out var mappedTypeDef)) {
                    if (mappedTypeDef.Module != type.Module) {
                        return MonoModCommon.Structure.CreateTypeReference(mappedTypeDef, type.Module);
                    }
                    return mappedTypeDef;
                }
                else {
                    return type;
                }
            }
            public static MethodReference DeepMapMethodReference(MethodReference method, MapOption option) {
                var def = method.TryResolve();
                if (def is null && method.DeclaringType is ArrayType arrayType) {
                    arrayType = new ArrayType(DeepMapTypeReference(arrayType.ElementType, option), arrayType.Rank);
                    var mref = new MethodReference(method.Name, DeepMapTypeReference(method.ReturnType, option), arrayType) {
                        HasThis = method.HasThis
                    };
                    mref.Parameters.AddRange(method.Parameters.Select(p => new ParameterDefinition(DeepMapTypeReference(p.ParameterType, option))));
                    return mref;
                }
                else {
                    if (method is GenericInstanceMethod genericInstanceMethod) {
                        return DeepMapGenericInstanceMethod(genericInstanceMethod, option);
                    }
                    var declaringType = method.DeclaringType;
                    if (option.TypeReplaceMap.TryGetValue(declaringType.Resolve(), out var mappedDeclaringType) 
                        && mappedDeclaringType.Methods.Any(m => m.GetIdentifier(false) == method.GetIdentifier(false) 
                        && m.HasThis == method.HasThis)) {
                        declaringType = DeepMapTypeReference(method.DeclaringType, option);
                    }
                    var mref = new MethodReference(method.Name,
                        DeepMapTypeReference(method.ReturnType, option),
                        declaringType) {
                        HasThis = method.HasThis
                    };
                    mref.Parameters.AddRange(method.Parameters.Select(p => new ParameterDefinition(DeepMapTypeReference(p.ParameterType, option))));

                    return mref;
                }
            }
            /// <summary>
            /// Maps a method definition to a new method definition through the given type maps, and optionally maps its body
            /// <para>BE CAREFUL: This method won't add self to any potential declaring type</para>
            /// </summary>
            /// <param name="method"></param>
            /// <param name="shouldMapBody">Sometimes the methods will reference each other. so we can delay mapping the body until declarations of all methods are mapped</param>
            /// <returns></returns>
            public static MethodDefinition DeepMapMethodDef(MethodDefinition method, MapOption option, bool shouldMapBody) {

                if (option.MethodReplaceMap.TryGetValue(method, out var mappedMethod)) {
                    return mappedMethod;
                }
                var result = new MethodDefinition(method.Name, method.Attributes, method.Module.TypeSystem.Void);

                result.CustomAttributes.AddRange(method.CustomAttributes.Select(c => c.Clone()));

                foreach (var genParam in method.GenericParameters) {
                    result.GenericParameters.Add(genParam.Clone());
                }

                option.MethodReplaceMap.Add(method, result);

                for (int i = 0; i < method.GenericParameters.Count; i++) {
                    var from = method.GenericParameters[i];
                    var to = result.GenericParameters[i];

                    to.Constraints.Clear();
                    to.Constraints.AddRange(from.Constraints.Select(c => new GenericParameterConstraint(DeepMapTypeReference(c.ConstraintType, option))));
                }

                result.Name = method.Name;
                result.Attributes = method.Attributes;
                result.ReturnType = DeepMapTypeReference(method.ReturnType, option);

                foreach (var param in method.Parameters) {
                    var clonedParam = param.Clone();
                    clonedParam.ParameterType = DeepMapTypeReference(param.ParameterType, option);
                    result.Parameters.Add(clonedParam);
                }

                result.MetadataToken = result.MetadataToken;
                result.Attributes = method.Attributes;
                result.HasThis = method.HasThis;
                result.ImplAttributes = method.ImplAttributes;
                result.PInvokeInfo = method.PInvokeInfo;
                result.IsPreserveSig = method.IsPreserveSig;
                result.IsPInvokeImpl = method.IsPInvokeImpl;

                foreach (var attrib in method.CustomAttributes) {
                    result.CustomAttributes.Add(attrib.Clone());
                }

                foreach (var @override in method.Overrides) {
                    // TODO: implement
                    result.Overrides.Add(@override);
                }

                if (shouldMapBody) {
                    result.Body = DeepMapMethodBody(method, result, option);
                }

                return result;
            }
            public static MethodBody? DeepMapMethodBody(MethodDefinition from, MethodDefinition to, MapOption option) {
                if (from.Body is null) {
                    return null;
                }

                MethodBody copied = new(to);
                var copyFrom = from.Body;

                copied.MaxStackSize = copyFrom.MaxStackSize;
                copied.InitLocals = copyFrom.InitLocals;
                copied.LocalVarToken = copyFrom.LocalVarToken;

                Dictionary<Instruction, Instruction> instMap = new();
                Dictionary<VariableDefinition, VariableDefinition> varMap = new();

                foreach (var local in copyFrom.Variables) {
                    var addLocal = new VariableDefinition(DeepMapTypeReference(local.VariableType, option));
                    copied.Variables.Add(addLocal);
                    varMap.Add(local, addLocal);
                }

                foreach (var inst in copyFrom.Instructions) {
                    var addInst = Instruction.Create(OpCodes.Nop);
                    addInst.OpCode = inst.OpCode;
                    addInst.Operand = inst.Operand;
                    addInst.Offset = inst.Offset;

                    copied.Instructions.Add(addInst);
                    instMap.Add(inst, addInst);
                }

                foreach (var inst in copied.Instructions) {
                    int foundIndex;
                    if (inst.Operand is Instruction target) {
                        inst.Operand = instMap[target];
                    }
                    else if (inst.Operand is Instruction[] targets) {
                        var newTargets = new Instruction[targets.Length];
                        for (int i = 0; i < targets.Length; i++) {
                            newTargets[i] = instMap[targets[i]];
                        }
                        inst.Operand = newTargets;
                    }
                    else if (inst.Operand is TypeReference typeRef) {
                        inst.Operand = DeepMapTypeReference(typeRef, option);
                    }
                    else if (inst.Operand is MethodReference methodRef) {
                        inst.Operand = DeepMapMethodReference(methodRef, option);
                    }
                    else if (inst.Operand is FieldReference fieldRef) {
                        var fieldDef = fieldRef.Resolve();
                        if (fieldDef is null) {
                            continue;
                        }
                        var declaringType = fieldRef.DeclaringType;
                        if (option.TypeReplaceMap.TryGetValue(declaringType.Resolve(), out var mappedDeclaringType)
                            && mappedDeclaringType.Fields.Any(f => f.Name == fieldDef.Name && f.IsStatic == fieldDef.IsStatic)) {
                            declaringType = DeepMapTypeReference(fieldRef.DeclaringType, option);
                        }
                        inst.Operand = new FieldReference(fieldRef.Name,
                            DeepMapTypeReference(fieldRef.FieldType, option),
                            declaringType);
                    }
                    else if (inst.Operand is GenericParameter genParam && (foundIndex = from.GenericParameters.IndexOf(genParam)) != -1) {
                        inst.Operand = copied.Method.GenericParameters[foundIndex];
                    }
                    else if (inst.Operand is ParameterDefinition param && (foundIndex = from.Parameters.IndexOf(param)) != -1) {
                        inst.Operand = copied.Method.Parameters[foundIndex];
                    }
                    else if (inst.Operand is VariableDefinition vardef) {
                        inst.Operand = varMap[vardef];
                    }
                }

                copied.ExceptionHandlers.AddRange(copyFrom.ExceptionHandlers.Select(o => {
                    var c = new ExceptionHandler(o.HandlerType);
                    c.TryStart = o.TryStart is null ? null : copied.Instructions[copyFrom.Instructions.IndexOf(o.TryStart)];
                    c.TryEnd = o.TryEnd is null ? null : copied.Instructions[copyFrom.Instructions.IndexOf(o.TryEnd)];
                    c.FilterStart = o.FilterStart is null ? null : copied.Instructions[copyFrom.Instructions.IndexOf(o.FilterStart)];
                    c.HandlerStart = o.HandlerStart is null ? null : copied.Instructions[copyFrom.Instructions.IndexOf(o.HandlerStart)];
                    c.HandlerEnd = o.HandlerEnd is null ? null : copied.Instructions[copyFrom.Instructions.IndexOf(o.HandlerEnd)];
                    c.CatchType = o.CatchType;
                    return c;
                }));

                Instruction ResolveInstrOff(int off) {
                    // Can't check cloned instruction offsets directly, as those can change for some reason
                    for (var i = 0; i < copyFrom.Instructions.Count; i++)
                        if (copyFrom.Instructions[i].Offset == off)
                            return copied.Instructions[i];
                    throw new ArgumentException($"Invalid instruction offset {off}");
                }

                copied.Method.CustomDebugInformations.AddRange(copyFrom.Method.CustomDebugInformations.Select(o => {
                    if (o is AsyncMethodBodyDebugInformation ao) {
                        var c = new AsyncMethodBodyDebugInformation();
                        if (ao.CatchHandler.Offset >= 0)
                            c.CatchHandler = ao.CatchHandler.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(ao.CatchHandler.Offset));
                        c.Yields.AddRange(ao.Yields.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                        c.Resumes.AddRange(ao.Resumes.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                        c.ResumeMethods.AddRange(ao.ResumeMethods);
                        return c;
                    }
                    else if (o is StateMachineScopeDebugInformation so) {
                        var c = new StateMachineScopeDebugInformation();
                        c.Scopes.AddRange(so.Scopes.Select(s => new StateMachineScope(ResolveInstrOff(s.Start.Offset), s.End.IsEndOfMethod ? null : ResolveInstrOff(s.End.Offset))));
                        return c;
                    }
                    else
                        return o;
                }));

                copied.Method.DebugInformation.SequencePoints.AddRange(copyFrom.Method.DebugInformation.SequencePoints.Select(o => {
                    var c = new SequencePoint(ResolveInstrOff(o.Offset), o.Document);
                    c.StartLine = o.StartLine;
                    c.StartColumn = o.StartColumn;
                    c.EndLine = o.EndLine;
                    c.EndColumn = o.EndColumn;
                    return c;
                }));

                return copied;
            }

            public static MethodReference CreateTypedMethod(MethodReference impl) {
                Dictionary<GenericParameter, TypeReference> map = [];

                MethodReference patten;

                if (impl is GenericInstanceMethod genericInstanceMethod) {
                    patten = genericInstanceMethod.ElementMethod;
                    for (var i = 0; i < genericInstanceMethod.GenericArguments.Count; i++) {
                        map.Add(patten.GenericParameters[i], genericInstanceMethod.GenericArguments[i]);
                    }
                }
                else if (impl.DeclaringType is GenericInstanceType genericInstanceType) {
                    var elementType = genericInstanceType.ElementType;
                    patten = impl;
                    for (var i = 0; i < genericInstanceType.GenericArguments.Count; i++) {
                        map.Add(elementType.GenericParameters[i], genericInstanceType.GenericArguments[i]);
                    }
                }
                else {
                    return impl;
                }

                var option = new MapOption(genericParameterMap: map);
                return DeepMapMethodReference(patten, option);
            }
            public static MethodReference CreateTypedMethod(MethodDefinition methodDef, TypeReference declaringType) {
                Dictionary<GenericParameter, TypeReference> map = [];

                if (declaringType is GenericInstanceType genericInstanceType) {
                    var elementType = genericInstanceType.ElementType;
                    for (var i = 0; i < genericInstanceType.GenericArguments.Count; i++) {
                        map.Add(elementType.GenericParameters[i], genericInstanceType.GenericArguments[i]);
                    }
                }
                else {
                    return methodDef;
                }

                var option = new MapOption(genericParameterMap: map);
                return DeepMapMethodReference(methodDef, option);
            }
        }
    }
}
