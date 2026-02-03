using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching
{
    public static class PatchingCommon
    {
        public static bool IsDelegateInjectedCtxParam(TypeReference type) {
            if (!type.IsDelegate()) {
                return false;
            }
            if (type is GenericInstanceType genericInstance) {
                var declaringType = genericInstance.ElementType.Resolve();
                var paramType = declaringType.GetMethod(nameof(Action.Invoke)).Parameters.FirstOrDefault()?.ParameterType;
                if (paramType is null) {
                    return false;
                }
                if (paramType.FullName is Constants.RootContextFullName) {
                    return true;
                }
                if (paramType is not GenericParameter genericParam) {
                    return false;
                }
                var index = declaringType.GenericParameters.IndexOf(genericParam);
                if (index == -1) {
                    return false;
                }
                return genericInstance.GenericArguments[index].FullName is Constants.RootContextFullName;
            }
            else {
                var declaringType = type.Resolve();
                return declaringType.GetMethod(nameof(Action.Invoke)).Parameters.FirstOrDefault()
                    ?.ParameterType.FullName is Constants.RootContextFullName;
            }
        }
        public static MethodDefinition CreateInstanceConvdMethod(MethodDefinition staticMethod, ContextTypeData instanceConvdType, ImmutableDictionary<string, FieldDefinition> instanceConvdFieldOrgiMap) {
            if (!staticMethod.IsStatic) {
                throw new ArgumentException("Method must be static", nameof(staticMethod));
            }
            if (staticMethod.IsConstructor) {
                throw new ArgumentException("Static constructor is not supported", nameof(staticMethod));
            }
            if (!staticMethod.HasBody) {
                throw new ArgumentException("Method must have body", nameof(staticMethod));
            }
            if (instanceConvdType.originalType.FullName != staticMethod.DeclaringType.FullName) {
                throw new ArgumentException($"Method must be in the same type as the instanceConvd original type '{instanceConvdType.originalType.FullName}'", nameof(staticMethod));
            }

            MethodDefinition newMethod;

            if (instanceConvdType.IsReusedSingleton) {
                newMethod = staticMethod;
                InsertParamAt0AndRemapIndices(newMethod.Body, InsertParamMode.MakeInstance);
            }
            else {
                newMethod = new MethodDefinition(staticMethod.Name, staticMethod.Attributes, staticMethod.ReturnType);
                newMethod.CustomAttributes.AddRange(staticMethod.CustomAttributes.Select(c => c.Clone()));

                newMethod.Body = staticMethod.Body.Clone(newMethod);

                foreach (GenericParameter genericParameter in staticMethod.GenericParameters) {
                    newMethod.GenericParameters.Add(genericParameter.Clone());
                }
                foreach (ParameterDefinition parameter in staticMethod.Parameters) {
                    var param = parameter.Clone();
                    newMethod.Parameters.Add(param);
                }

                foreach (Instruction instruction in newMethod.Body.Instructions) {
                    int index;
                    if (instruction.Operand is GenericParameter item && (index = staticMethod.GenericParameters.IndexOf(item)) != -1) {
                        instruction.Operand = newMethod.GenericParameters[index];
                    }
                    else if (instruction.Operand is ParameterDefinition item2 && (index = staticMethod.Parameters.IndexOf(item2)) != -1) {
                        instruction.Operand = newMethod.Parameters[index];
                    }
                }

                InsertParamAt0AndRemapIndices(newMethod.Body, InsertParamMode.MakeInstance);

                newMethod.DeclaringType = instanceConvdType.ContextTypeDef;
                var id = newMethod.GetIdentifier();
                if (instanceConvdType.ContextTypeDef.Methods.Any(m => m.GetIdentifier() == id)) {
                    throw new Exception($"The method {staticMethod.GetDebugName()} has already been instanceConvd and added in the instanceConvd type");
                }
            }

            if (!instanceConvdType.IsReusedSingleton) {
                instanceConvdType.ContextTypeDef.Methods.Add(newMethod);
            }
            return newMethod;
        }
        public enum InsertParamMode
        {
            MakeInstance,
            Insert,
        }
        public static void InsertParamAt0AndRemapIndices(MethodBody body, InsertParamMode mode, ParameterDefinition? definition = null) {
            if (body.Method.Name is "DyeInitializer_LoadLegacyHairdyes") {

            }
            if (mode is InsertParamMode.Insert) {
                if (definition is null) {
                    throw new ArgumentNullException($"The {nameof(definition)} is required when {nameof(mode)} is {InsertParamMode.Insert}");
                }
                if (body.Method.HasOverrides) {
                    foreach (var overrides in body.Method.Overrides) {
                        overrides.Parameters.Insert(0, new ParameterDefinition("", definition.Attributes, definition.ParameterType));
                    }
                }
                body.Method.Parameters.Insert(0, definition);
            }
            else if (mode is InsertParamMode.MakeInstance) {
                if (!body.Method.IsStatic) {
                    throw new ArgumentException($"The {nameof(body)} must be static when {nameof(mode)} is {InsertParamMode.MakeInstance}");
                }
                if (body.Method.DeclaringType is not null && body.Method.DeclaringType.Attributes.HasFlag(TypeAttributes.Abstract | TypeAttributes.Sealed)) {
                    throw new ArgumentException($"The {nameof(body)} must define in a non-static class when {nameof(mode)} is {InsertParamMode.MakeInstance}");
                }
                body.Method.IsStatic = false;
                body.Method.HasThis = true;
            }
            else {
                throw new ArgumentOutOfRangeException($"The {nameof(mode)} must be {InsertParamMode.MakeInstance} or {InsertParamMode.Insert}");
            }
            foreach (Instruction instruction in body.Instructions) {
                switch (instruction.OpCode.Code) {
                    case Code.Ldarg_0:
                        // If original method is instance method, the inserted-param will be the second argument (include 'this')
                        // so we only remap the first argument when the original method is static
                        if (mode is InsertParamMode.MakeInstance || (mode is InsertParamMode.Insert && body.Method.IsStatic)) {
                            instruction.OpCode = OpCodes.Ldarg_1;
                        }
                        break;
                    case Code.Ldarg_1:
                        instruction.OpCode = OpCodes.Ldarg_2;
                        break;
                    case Code.Ldarg_2:
                        instruction.OpCode = OpCodes.Ldarg_3;
                        break;
                    case Code.Ldarg_3:
                        instruction.OpCode = OpCodes.Ldarg_S;

                        int index;
                        // The Parameter pointed to by Ldarg_3 is marked by <>
                        // based on the following three case, we can infer the value of index.

                        if (mode is InsertParamMode.MakeInstance) {
                            // static -> instance (inserted-param as this):
                            // [[arg0, arg1, arg2, <arg3>]] -> [this, [arg0, arg1, arg2, <arg3>]]
                            index = 3;
                        }
                        else if (body.Method.IsStatic) {
                            // static -> static (add inserted-param at first):
                            // [[arg0, arg1, arg2, <arg3>]] -> [[param, arg0, arg1, arg2, <arg3>]]
                            index = 4;
                        }
                        else {
                            // instance -> instance (add inserted-param after 'this'):
                            // [this, [arg0, arg1, <arg2>]] -> [this, [param, arg0, arg1, <arg2>]]
                            index = 3;
                        }

                        instruction.Operand = body.Method.Parameters[index];
                        break;
                    case Code.Ldarg_S:
                    case Code.Ldarga_S:
                    case Code.Starg_S:
                        var param = (ParameterDefinition)instruction.Operand;
                        if (param.Index >= 255 || param.Index == -1 && body.Method.Parameters.IndexOf(param) >= 255) {
                            instruction.OpCode = instruction.OpCode.Code switch {
                                Code.Ldarg_S => OpCodes.Ldarg,
                                Code.Ldarga_S => OpCodes.Ldarga,
                                Code.Starg_S => OpCodes.Starg,
                                _ => throw new InvalidOperationException(),
                            };
                        }
                        break;
                }
            }
        }
        public static void InsertParamAndRemapIndices(MethodBody body, int index, ParameterDefinition definition) {
            body.Method.Parameters.Insert(index, definition);
            if (body.Method.HasOverrides) {
                foreach (var overrides in body.Method.Overrides) {
                    overrides.Parameters.Insert(index, new ParameterDefinition("", definition.Attributes, definition.ParameterType));
                }
            }

            foreach (Instruction instruction in body.Instructions) {
                switch (instruction.OpCode.Code) {
                    case Code.Ldarg_0:
                        if (index == 0 && body.Method.IsStatic) {
                            instruction.OpCode = OpCodes.Ldarg_1;
                        }
                        break;
                    case Code.Ldarg_1:
                        if (index <= 1) {
                            instruction.OpCode = OpCodes.Ldarg_2;
                        }
                        break;
                    case Code.Ldarg_2:
                        if (index <= 2) {
                            instruction.OpCode = OpCodes.Ldarg_3;
                        }
                        break;
                    case Code.Ldarg_3:
                        if (index <= 3) {
                            instruction.OpCode = OpCodes.Ldarg_S;
                            int newindex;
                            if (body.Method.IsStatic) {
                                // static -> static (add inserted-param at first):
                                // [[arg0, arg1, arg2, <arg3>]] -> [[param, arg0, arg1, arg2, <arg3>]]
                                newindex = 4;
                            }
                            else {
                                // instance -> instance (add inserted-param after 'this'):
                                // [this, [arg0, arg1, <arg2>]] -> [this, [param, arg0, arg1, <arg2>]]
                                newindex = 3;
                            }
                            instruction.Operand = body.Method.Parameters[newindex];
                        }
                        break;
                    case Code.Ldarg_S:
                    case Code.Ldarga_S:
                    case Code.Starg_S:
                        var param = (ParameterDefinition)instruction.Operand;
                        if (param.Index >= 255 || param.Index == -1 && body.Method.Parameters.IndexOf(param) >= 255) {
                            instruction.OpCode = instruction.OpCode.Code switch {
                                Code.Ldarg_S => OpCodes.Ldarg,
                                Code.Ldarga_S => OpCodes.Ldarga,
                                Code.Starg_S => OpCodes.Starg,
                                _ => throw new InvalidOperationException(),
                            };
                        }
                        break;
                }
            }
        }
        public enum RemoveParamMode
        {
            MakeStatic,
            Remove,
        }
        public static void RemoveParamAt0AndRemapIndices(MethodBody body, RemoveParamMode mode) {
            if (mode is RemoveParamMode.MakeStatic) {
                if (body.Method.IsStatic) {
                    throw new ArgumentException($"The {nameof(body)} must be static when {nameof(mode)} is {InsertParamMode.MakeInstance}");
                }
                body.Method.IsStatic = true;
                body.Method.HasThis = false;
            }
            else {
                if (body.Method.Parameters.Count == 0) {
                    throw new ArgumentException($"The {nameof(body)} must have at least one TracingParameter when {nameof(mode)} is {RemoveParamMode.Remove}");
                }
            }

            foreach (Instruction instruction in body.Instructions) {
                switch (instruction.OpCode.Code) {

                    // The Parameter will be removed is marked by <>
                    // based on the following three case, we can infer the value of index.

                    // static -> instance:
                    // [<this>, [arg1, arg2, arg3, arg4]] -> [[arg1, arg2, arg3, arg4]]

                    // static -> static:
                    // [[<param>, arg1, arg2, arg3, arg4]] -> [[arg1, arg2, arg3, arg4]]

                    // instance -> instance:
                    // [this, [<param>, arg2, arg3, arg4]] -> [this, [arg2, arg3, arg4]]

                    case Code.Ldarg_0:
                        // If original method is instance method, the inserted-param will be the second argument (include 'this')
                        // so we only remap the first argument when the original method is static
                        if (mode is RemoveParamMode.MakeStatic) {
                            throw new ArgumentException($"The reference to the 'this' must be removed when {nameof(mode)} is {RemoveParamMode.MakeStatic}");
                        }
                        else if (mode is RemoveParamMode.Remove && body.Method.IsStatic) {
                            throw new ArgumentException($"The reference to the first argument must be removed when {nameof(mode)} is {RemoveParamMode.Remove}");
                        }
                        break;
                    case Code.Ldarg_1:
                        if (mode is RemoveParamMode.Remove && !body.Method.IsStatic) {
                            throw new ArgumentException($"The reference to the first argument (besides 'this') must be removed when {nameof(mode)} is {RemoveParamMode.Remove}");
                        }
                        instruction.OpCode = OpCodes.Ldarg_0;
                        break;
                    case Code.Ldarg_2:
                        instruction.OpCode = OpCodes.Ldarg_1;
                        break;
                    case Code.Ldarg_3:
                        instruction.OpCode = OpCodes.Ldarg_2;
                        break;
                    case Code.Ldarg_S:
                    case Code.Ldarga_S:
                    case Code.Starg_S:
                        var param = (ParameterDefinition)instruction.Operand;
                        if (mode is RemoveParamMode.Remove && param.Index == 0) {
                            throw new ArgumentException($"The reference to the first argument must be removed when {nameof(mode)} is {RemoveParamMode.Remove}");
                        }

                        if (param.Index == 256 || param.Index == -1 && body.Method.Parameters.IndexOf(param) == 256) {
                            instruction.OpCode = instruction.OpCode.Code switch {
                                Code.Ldarg => OpCodes.Ldarg_S,
                                Code.Ldarga => OpCodes.Ldarga_S,
                                Code.Starg => OpCodes.Starg_S,
                                _ => throw new InvalidOperationException(),
                            };
                        }
                        break;
                }
            }
            if (mode is RemoveParamMode.Remove) {
                body.Method.Parameters.RemoveAt(0);
            }
        }
        public static void InsertAtVar0AndRemapIndices(MethodBody body, VariableDefinition variable) {
            body.Variables.Insert(0, variable);

            foreach (Instruction instruction in body.Instructions) {
                switch (instruction.OpCode.Code) {

                    case Code.Stloc_0:
                        instruction.OpCode = OpCodes.Stloc_1;
                        break;
                    case Code.Stloc_1:
                        instruction.OpCode = OpCodes.Stloc_2;
                        break;
                    case Code.Stloc_2:
                        instruction.OpCode = OpCodes.Stloc_3;
                        break;

                    case Code.Ldloc_0:
                        instruction.OpCode = OpCodes.Ldloc_1;
                        break;
                    case Code.Ldloc_1:
                        instruction.OpCode = OpCodes.Ldloc_2;
                        break;
                    case Code.Ldloc_2:
                        instruction.OpCode = OpCodes.Ldloc_3;
                        break;

                    case Code.Stloc_3:
                        instruction.OpCode = OpCodes.Stloc_S;
                        instruction.Operand = body.Variables[4];
                        break;
                    case Code.Ldloc_3:
                        instruction.OpCode = OpCodes.Ldloc_S;
                        instruction.Operand = body.Variables[4];
                        break;
                    case Code.Ldloc_S:
                    case Code.Ldloca_S:
                    case Code.Stloc_S:
                        var useVariabe = (VariableDefinition)instruction.Operand;
                        if (useVariabe.Index >= 255 || useVariabe.Index == -1 && body.Variables.IndexOf(useVariabe) >= 255) {
                            instruction.OpCode = instruction.OpCode.Code switch {
                                Code.Ldloca_S => OpCodes.Ldloca,
                                Code.Stloc_S => OpCodes.Stloc,
                                Code.Ldloc_S => OpCodes.Ldloc,
                                _ => throw new InvalidOperationException(),
                            };
                        }
                        break;
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="arguments">patching data</param>
        /// <param name="callerBody">the body of the method that loads the context</param>
        /// <param name="instanceConvdType">null if loading root context</param>
        /// <param name="addedParam">if a context Parameter is added to the method</param>
        /// <returns></returns>
        public static Instruction[] BuildInstanceLoadInstrs(
            PatcherArguments arguments,
            MethodBody callerBody,
            ContextTypeData? instanceConvdType,
            out bool addedParam) {

            addedParam = false;

            // If method is an context-bound method and declaring type is the given instance-converted type
            // so it can directly use 'this'
            if (instanceConvdType is not null && callerBody.Method.DeclaringType.FullName == instanceConvdType.ContextTypeDef.FullName) {
                return [Instruction.Create(OpCodes.Ldarg_0)];
            }

            List<Instruction> result = [];

            if (!callerBody.Method.IsStatic && arguments.RootContextFieldToAdaptExternalInterface.TryGetValue(callerBody.Method.DeclaringType.FullName, out var rootContextField)) {
                if (callerBody.Method.IsConstructor) {
                    result.Add(Instruction.Create(OpCodes.Ldarg_1));
                }
                else {
                    result.Add(Instruction.Create(OpCodes.Ldarg_0));
                    result.Add(Instruction.Create(OpCodes.Ldfld, rootContextField));
                }
            }

            // If method is an context-bound method, we could create a variable of root context to easily access
            // unless the method is the constructor, then just use root context param
            else if (arguments.ContextTypes.TryGetValue(callerBody.Method.DeclaringType.FullName, out var callerDeclaringInstanceConvdType)
                && callerBody.Method != callerDeclaringInstanceConvdType.constructor
                && !callerBody.Method.IsConstructor) {

                VariableDefinition variable;
                if (callerBody.Variables.Count == 0 || callerBody.Variables[0].VariableType.FullName != arguments.RootContextDef.FullName) {
                    variable = new VariableDefinition(arguments.RootContextDef);
                    InsertAtVar0AndRemapIndices(callerBody, variable);

                    var processor = callerBody.GetILProcessor();
                    var firstInst = callerBody.Instructions[0];
                    processor.InsertBefore(firstInst, Instruction.Create(OpCodes.Ldarg_0));
                    processor.InsertBefore(firstInst, Instruction.Create(OpCodes.Ldfld, callerDeclaringInstanceConvdType.rootContextField));
                    processor.InsertBefore(firstInst, MonoModCommon.IL.BuildVariableStore(callerBody.Method, callerBody, variable));
                }
                else {
                    variable = callerBody.Variables[0];
                }
                result.Add(MonoModCommon.IL.BuildVariableLoad(callerBody.Method, callerBody, variable));
            }
            // If we do not create a variable of root context, the first Parameter in parameters must be the root context
            else {
                if (callerBody.Method.Parameters.Count == 0 || callerBody.Method.Parameters[0].ParameterType.FullName != arguments.RootContextDef.FullName) {
                    InsertParamAt0AndRemapIndices(callerBody, InsertParamMode.Insert, new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                    addedParam = true;
                }

                result.Add(MonoModCommon.IL.BuildParameterLoad(callerBody.Method, callerBody, callerBody.Method.Parameters[0]));
            }

            if (instanceConvdType is not null) {
                foreach (var field in instanceConvdType.nestedChain) {
                    result.Add(Instruction.Create(OpCodes.Ldfld, field));
                }
            }

            return [.. result];
        }
        public static MethodReference CreateMethodReference(MethodReference origiReference, MethodDefinition definition) {
            TypeReference declaringType = definition.DeclaringType;
            if (origiReference.DeclaringType is GenericInstanceType origiGenericType) {
                var genericType = new GenericInstanceType(declaringType);
                foreach (var genArg in origiGenericType.GenericArguments) {
                    genericType.GenericArguments.Add(genArg);
                }
                declaringType = genericType;
            }
            var callee = new MethodReference(definition.Name, definition.ReturnType, declaringType) {
                HasThis = definition.HasThis
            };
            foreach (var genParam in definition.GenericParameters) {
                callee.GenericParameters.Add(genParam.Clone());
            }
            foreach (var param in definition.Parameters) {
                callee.Parameters.Add(param.Clone());
            }
            if (origiReference is GenericInstanceMethod genericMethod) {
                var gerericCallee = new GenericInstanceMethod(callee);
                foreach (var genArg in genericMethod.GenericArguments) {
                    gerericCallee.GenericArguments.Add(genArg);
                }
                callee = gerericCallee;
            }

            return callee;
        }
        public static MethodReference GetVanillaMethodRef(TypeDefinition rootContextType, ImmutableDictionary<string, ContextTypeData> instanceConvdTypes, MethodReference method) {
            TypeReference declaringType = method.DeclaringType;
            bool hasThis = method.HasThis;

            if (instanceConvdTypes.TryGetValue(method.DeclaringType.FullName, out var instanceConvdType)) {
                var originalTypeDef = instanceConvdType.originalType;
                declaringType = new TypeReference(originalTypeDef.Namespace, originalTypeDef.Name, originalTypeDef.Module, originalTypeDef.Scope);
                declaringType.GenericParameters.AddRange(method.DeclaringType.GenericParameters.Select(p => p.Clone()));

                if (method.DeclaringType is GenericInstanceType genInstanceType) {
                    declaringType.GenericParameters.Clear();
                    declaringType.GenericParameters.AddRange(genInstanceType.ElementType.GenericParameters.Select(p => p.Clone()));
                    var copiedGenInstanceType = new GenericInstanceType(declaringType);
                    copiedGenInstanceType.GenericArguments.AddRange(genInstanceType.GenericArguments);
                    declaringType = copiedGenInstanceType;
                }
            }

            var vanilla = new MethodReference(method.Name, method.ReturnType, declaringType) {
                HasThis = hasThis
            };

            // Clone parameters excluding context parameters
            foreach (var param in method.Parameters) {
                if (param.ParameterType is GenericParameter genericParameter) {
                    if (genericParameter.Owner is MethodReference genericOwnerMethod) {
                        if (method is GenericInstanceMethod gMethod && 
                            gMethod.GenericArguments[genericParameter.Position].FullName == rootContextType.FullName) {
                            continue;
                        }
                    }
                    else {
                        if (method.DeclaringType is GenericInstanceType gType &&
                            gType.GenericArguments[genericParameter.Position].FullName == rootContextType.FullName) {
                            continue;
                        }
                    }
                }
                else if (param.ParameterType.FullName == rootContextType.FullName) {
                    continue; // Skip context Parameter for stack integrity
                }
                vanilla.Parameters.Add(param.Clone());
            }

            // Preserve generic structure
            vanilla.GenericParameters.AddRange(method.GenericParameters.Select(p => p.Clone()));

            if (method is GenericInstanceMethod genericMethod) {
                vanilla = new GenericInstanceMethod(method);
                ((GenericInstanceMethod)vanilla).GenericArguments.AddRange(genericMethod.GenericArguments);
            }

            return vanilla;
        }
        public static TypeDefinition MemberClonedType(TypeDefinition type, string newName, Dictionary<TypeDefinition, TypeDefinition>? mappedTypes = null, Dictionary<MethodDefinition, MethodDefinition>? mappedMethods = null) {
            mappedTypes ??= [];
            mappedMethods ??= [];
            var inputTypes = mappedTypes.ToDictionary();
            var mapCondition = new MonoModCommon.Structure.MapOption(mappedTypes, mappedMethods, [], []);

            static TypeDefinition ClonedType(TypeDefinition type, string newName, Dictionary<TypeDefinition, TypeDefinition> mappedTypes) {

                var copied = new TypeDefinition(type.Namespace, newName, type.Attributes, type.BaseType);
                mappedTypes.Add(type, copied);

                foreach (var nested in type.NestedTypes) {
                    ClonedType(nested, nested.Name, mappedTypes);
                }

                copied.GenericParameters.AddRange(type.GenericParameters.Select(p => p.Clone()));
                copied.CustomAttributes.AddRange(type.CustomAttributes);

                if (type.DeclaringType is not null) {
                    var declaringType = type.DeclaringType;
                    if (mappedTypes.TryGetValue(declaringType, out var copiedDeclaringType)) {
                        declaringType = copiedDeclaringType;
                    }
                    declaringType.NestedTypes.Add(copied);
                }
                else {
                    type.Module.Types.Add(copied);
                }

                return copied;
            }
            static void ClonedMember(TypeDefinition from,
                MonoModCommon.Structure.MapOption mapContext) {
                var copied = mapContext.TypeReplaceMap[from];

                foreach (var interfaceImpl in from.Interfaces) {
                    copied.Interfaces.Add(new InterfaceImplementation(MonoModCommon.Structure.DeepMapTypeReference(interfaceImpl.InterfaceType, mapContext)));
                }

                foreach (var field in from.Fields) {
                    var copiedField = new FieldDefinition(field.Name, field.Attributes, MonoModCommon.Structure.DeepMapTypeReference(field.FieldType, mapContext));
                    copied.Fields.Add(copiedField);
                }

                foreach (var method in from.Methods) {
                    var copiedMethod = MonoModCommon.Structure.DeepMapMethodDef(method, mapContext, false);
                    copied.Methods.Add(copiedMethod);
                }

                foreach (var property in from.Properties) {
                    copied.Properties.Add(new PropertyDefinition(property.Name, property.Attributes, property.PropertyType) {
                        GetMethod = property.GetMethod is null ? null : mapContext.MethodReplaceMap[property.GetMethod],
                        SetMethod = property.SetMethod is null ? null : mapContext.MethodReplaceMap[property.SetMethod]
                    });
                }

                foreach (var _event in from.Events) {
                    copied.Events.Add(new EventDefinition(_event.Name, _event.Attributes, _event.EventType) {
                        AddMethod = _event.AddMethod is null ? null : mapContext.MethodReplaceMap[_event.AddMethod],
                        RemoveMethod = _event.RemoveMethod is null ? null : mapContext.MethodReplaceMap[_event.RemoveMethod]
                    });
                }
            }

            var copied = ClonedType(type, newName, mappedTypes);
            foreach (var to in mappedTypes.Keys) {
                foreach (var gp in to.GenericParameters) {
                    for (int i = 0; i < gp.Constraints.Count; i++) {
                        var constraint = gp.Constraints[i];
                        var copiedConstraint = MonoModCommon.Structure.DeepMapTypeReference(constraint.ConstraintType, mapCondition);
                        gp.Constraints[i] = new GenericParameterConstraint(copiedConstraint);
                    }
                }
            }

            foreach (var from in mappedTypes.Keys) {
                if (inputTypes.ContainsKey(from)) {
                    continue;
                }
                ClonedMember(from, mapCondition);
            }

            foreach (var kv in mappedMethods) {
                kv.Value.Body = MonoModCommon.Structure.DeepMapMethodBody(kv.Key, kv.Value, mapCondition);
            }

            return copied;
        }
    }
}
