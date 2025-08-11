using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess
{
    public class ModAssemblyMerger
    {
        readonly Dictionary<string, ModuleDefinition> modModules = [];
        public ModAssemblyMerger(params System.Reflection.Assembly[] mods) {
            foreach (var assembly in mods) {
                var mod = AssemblyDefinition.ReadAssembly(assembly.Location);
                modModules.TryAdd(mod.FullName, mod.MainModule);
            }
        }
        public void Attach(ModContext context) {
            context.OnApply += (progress, modder) => {
                if (modder is null) {
                    return ModContext.EApplyResult.Continue;
                }
                var module = modder.Module;
                if (progress == ModType.PreRead) {
                }
                else if (progress == ModType.PrePatch) {
                    var modderTypes = module.GetAllTypes().ToDictionary(x => x.FullName, x => x);
                    Dictionary<string, TypeDefinition> modsTypes = [];
                    foreach (var mod in modModules.Values) {
                        foreach (var type in mod.Types) {
                            SetModTypePlaceholder(module, modderTypes, type, null);
                        }
                        modder.PrePatchModule(mod);
                        modder.PatchModule(mod);
                        modderTypes = module.GetAllTypes().ToDictionary(x => x.FullName, x => x);

                        foreach (var type in mod.GetAllTypes()) {
                            var mappedType = module.GetType(type.FullName) ?? throw new NotSupportedException();
                            AdjustTypeInfo(module, mod, type, mappedType);
                            AdjustInterfaces(module, mod, type, mappedType);
                            AdjustMembers(module, mod, type, mappedType);
                            AdjustInstructions(module, mod, type, mappedType);
                        }
                    }
                }
                return ModContext.EApplyResult.Continue;
            };
        }

        private static void AdjustTypeInfo(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            mappedType.Attributes = type.Attributes;
            //if (mappedType.DeclaringType is not null && mappedType.DeclaringType.Name == "<PrivateImplementationDetails>") {
            //    mappedType.Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed;
            //}
            if (mappedType.IsStruct()) {
                mappedType.ClassSize = type.ClassSize;
                mappedType.PackingSize = type.PackingSize;
            }
            if (type.BaseType is not null) {
                var baseType = type.BaseType;
                RedirectTypeRef(target, mod, ref baseType);
                mappedType.BaseType = baseType;
            }
            if (type.HasGenericParameters) {
                mappedType.GenericParameters.Clear();
                foreach (var param in type.GenericParameters) {
                    var gp = new GenericParameter(param.Name, mappedType);
                    foreach (var constraint in param.Constraints) {
                        var constraintType = constraint.ConstraintType;
                        RedirectTypeRef(target, mod, ref constraintType);
                        gp.Constraints.Add(new GenericParameterConstraint(constraintType));
                    }
                    mappedType.GenericParameters.Add(gp);

                    foreach (var att in param.CustomAttributes) {
                        var mapped = MapCustomAttribute(target, mod, att);
                        if (mapped is not null) {
                            gp.CustomAttributes.Add(mapped);
                        }
                    }
                }
            }
            AdjustMemberAttributes(target, mod, mappedType.CustomAttributes);
        }

        private static void AdjustMembers(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (var field in mappedType.Fields.ToArray()) {
                var fieldType = field.FieldType;
                if (RedirectTypeRef(target, mod, ref fieldType)) {
                    field.FieldType = fieldType;
                }
                AdjustMemberAttributes(target, mod, field.CustomAttributes);
            }
            foreach (var property in mappedType.Properties.ToArray()) {
                var propertyType = property.PropertyType;
                if (RedirectTypeRef(target, mod, ref propertyType)) {
                    property.PropertyType = propertyType;
                }
                AdjustMemberAttributes(target, mod, property.CustomAttributes);
            }
            foreach (var eventDef in mappedType.Events.ToArray()) {
                var eventType = eventDef.EventType;
                if (RedirectTypeRef(target, mod, ref eventType)) {
                    eventDef.EventType = eventType;
                }
                AdjustMemberAttributes(target, mod, eventDef.CustomAttributes);
            }
            foreach (var method in mappedType.Methods.ToArray()) {
                var methodType = method.ReturnType;
                if (RedirectTypeRef(target, mod, ref methodType)) {
                    method.ReturnType = methodType;
                }
                AdjustMemberAttributes(target, mod, method.MethodReturnType.CustomAttributes);
                foreach (var param in method.Parameters) {
                    var paramType = param.ParameterType;
                    if (RedirectTypeRef(target, mod, ref paramType)) {
                        param.ParameterType = paramType;
                    }
                    AdjustMemberAttributes(target, mod, param.CustomAttributes);
                }
                foreach (var genericParam in method.GenericParameters) {
                    foreach (var constraint in genericParam.Constraints) {
                        var constraintType = constraint.ConstraintType;
                        if (RedirectTypeRef(target, mod, ref constraintType)) {
                            constraint.ConstraintType = constraintType;
                        }
                    }
                    AdjustMemberAttributes(target, mod, genericParam.CustomAttributes);
                }
                for (int i = 0; i < method.Overrides.Count; i++) {
                    var ovrride = method.Overrides[i];
                    if (ovrride is GenericInstanceMethod generic) {
                        var elementMethod = RedirectElementMethodRef(target, mod, generic.ElementMethod);
                        var mappedGeneric = new GenericInstanceMethod(elementMethod);
                        for (var j = 0; j < generic.GenericArguments.Count; j++) {
                            var arg = generic.GenericArguments[j];
                            RedirectTypeRef(target, mod, ref arg);
                            mappedGeneric.GenericArguments.Add(arg);
                        }
                        ovrride = mappedGeneric;
                    }
                    else {
                        ovrride = RedirectElementMethodRef(target, mod, ovrride);
                    }
                    method.Overrides[i] = ovrride;
                }
                if (method.HasBody) {
                    foreach (var local in method.Body.Variables) {
                        var localType = local.VariableType;
                        if (RedirectTypeRef(target, mod, ref localType)) {
                            local.VariableType = localType;
                        }
                    }
                    foreach (var ex in method.Body.ExceptionHandlers) {
                        var exType = ex.CatchType;
                        if (RedirectTypeRef(target, mod, ref exType)) {
                            ex.CatchType = exType;
                        }
                    }
                }
            }
        }

        static void AdjustInstructions(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (var method in mappedType.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (var inst in method.Body.Instructions) {
                    if (inst.Operand is not MemberReference mr) {
                        continue;
                    }
                    if (inst.Operand is FieldReference fieldRef) {
                        var declaringType = fieldRef.DeclaringType;
                        var fieldType = fieldRef.FieldType;
                        RedirectTypeRef(target, mod, ref declaringType);
                        RedirectTypeRef(target, mod, ref fieldType);
                        fieldRef = new FieldReference(fieldRef.Name, fieldType, declaringType);
                        inst.Operand = fieldRef;
                    }
                    else if (inst.Operand is MethodReference methodRef) {
                        if (methodRef is GenericInstanceMethod generic) {
                            var elementMethod = RedirectElementMethodRef(target, mod, generic.ElementMethod);
                            var mappedGeneric = new GenericInstanceMethod(elementMethod);
                            for (var i = 0; i < generic.GenericArguments.Count; i++) {
                                var arg = generic.GenericArguments[i];
                                RedirectTypeRef(target, mod, ref arg);
                                mappedGeneric.GenericArguments.Add(arg);
                            }
                            methodRef = mappedGeneric;
                        }
                        else {
                            methodRef = RedirectElementMethodRef(target, mod, methodRef);
                        }
                        inst.Operand = methodRef;
                    }
                    else if (inst.Operand is PropertyReference propRef) {
                        var declaringType = propRef.DeclaringType;
                        var propType = propRef.PropertyType;
                        RedirectTypeRef(target, mod, ref declaringType);
                        RedirectTypeRef(target, mod, ref propType);
                        propRef.DeclaringType = declaringType;
                        propRef.PropertyType = propType;
                    }
                    else if (inst.Operand is EventReference eventRef) {
                        var declaringType = eventRef.DeclaringType;
                        var eventType = eventRef.EventType;
                        RedirectTypeRef(target, mod, ref declaringType);
                        RedirectTypeRef(target, mod, ref eventType);
                        eventRef.DeclaringType = declaringType;
                        eventRef.EventType = eventType;
                    }
                    else if (inst.Operand is TypeReference typeRef) {
                        RedirectTypeRef(target, mod, ref typeRef);
                        inst.Operand = typeRef;
                    }
                    else if (inst.Operand is CallSite callSite) {
                        var returnType = callSite.ReturnType;
                        RedirectTypeRef(target, mod, ref returnType);
                        callSite.ReturnType = returnType;
                        foreach (var arg in callSite.Parameters) {
                            var argType = arg.ParameterType;
                            RedirectTypeRef(target, mod, ref argType);
                            arg.ParameterType = argType;
                        }
                    }
                    else if (inst.Operand is ParameterReference parameter) {
                        var parameterType = parameter.ParameterType;
                        RedirectTypeRef(target, mod, ref parameterType);
                        parameter.ParameterType = parameterType;
                    }
                    else if (inst.Operand is VariableReference variable) {
                        var variableType = variable.VariableType;
                        RedirectTypeRef(target, mod, ref variableType);
                        variable.VariableType = variableType;
                    }
                }
            }
        }

        static MethodReference RedirectElementMethodRef(ModuleDefinition target, ModuleDefinition mod, MethodReference methodRef) {
            var declaringType = methodRef.DeclaringType;
            var methodType = methodRef.ReturnType;
            RedirectTypeRef(target, mod, ref declaringType);
            RedirectTypeRef(target, mod, ref methodType);
            var mappedMethod = new MethodReference(methodRef.Name, methodType, declaringType) {
                HasThis = methodRef.HasThis
            };
            foreach (var param in methodRef.Parameters) {
                var paramType = param.ParameterType;
                RedirectTypeRef(target, mod, ref paramType);
                mappedMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, paramType));
            }
            if (methodRef.HasGenericParameters) {
                foreach (var genericParam in methodRef.GenericParameters) {
                    var mappedGenericParam = new GenericParameter(mappedMethod);
                    foreach (var constraint in genericParam.Constraints) {
                        var constraintType = constraint.ConstraintType;
                        RedirectTypeRef(target, mod, ref constraintType);
                        genericParam.Constraints.Add(new GenericParameterConstraint(constraintType));
                    }
                    mappedMethod.GenericParameters.Add(mappedGenericParam);
                }
            }

            return mappedMethod;
        }
        static void SetModTypePlaceholder(ModuleDefinition module, Dictionary<string, TypeDefinition> uspTypes, TypeDefinition modType, TypeDefinition? declaringType) {
            if (!uspTypes.TryGetValue(modType.FullName, out var target)) {
                target = new TypeDefinition(modType.Namespace, modType.Name, modType.Attributes, modType.BaseType) {
                    Attributes = modType.Attributes,
                };
                if (declaringType is not null) {
                    declaringType.NestedTypes.Add(target);
                }
                else {
                    module.Types.Add(target);
                }
                foreach (var method in modType.Methods) {
                    PrepareMethod(target, method, null);
                    SetMemberReplace(module, method.CustomAttributes, false);
                }
            }
            else {
                var existingMethods = target.Methods.ToDictionary(m => m.GetIdentifier(), m => m);
                foreach (var method in modType.Methods) {
                    if (!existingMethods.TryGetValue(method.GetIdentifier(), out var existingMethod)) {
                        existingMethod = null;
                    }
                    PrepareMethod(target, method, existingMethod);
                    if (!method.IsSpecialName) {
                        SetMemberReplace(module, method.CustomAttributes, false);
                    }
                }
                if (modType.IsEnum) {
                    SetMemberReplace(module, modType.CustomAttributes, true);
                }
                foreach (var field in modType.Fields) {
                    if (modType.IsEnum && !field.IsStatic) {
                        SetMemberReplace(module, field.CustomAttributes, false);
                    }
                    else {
                        SetMemberReplace(module, field.CustomAttributes, modType.IsEnum);
                    }
                }
            }
            foreach (var nested in modType.NestedTypes) {
                SetModTypePlaceholder(module, uspTypes, nested, target);
            }
        }
        static void PrepareMethod(TypeDefinition targetType, MethodDefinition modMethod, MethodDefinition? originalMethod) {
            if (modMethod.IsConstructor && !modMethod.IsStatic) {
                var attType_ctor = modMethod.Module.ImportReference(typeof(MonoMod.MonoModConstructor));
                modMethod.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", modMethod.Module.TypeSystem.Void, attType_ctor) { HasThis = true }));
            }
        }
        static void SetMemberReplace(ModuleDefinition module, Collection<CustomAttribute> attributes, bool isEnum) {
            var type = isEnum ? typeof(MonoMod.MonoModEnumReplace) : typeof(MonoMod.MonoModReplace);
            if (attributes.Any(a => a.AttributeType.Name == type.Name)) {
                return;
            }
            var attType_replace = module.ImportReference(type);
            attributes.Add(new CustomAttribute(new MethodReference(".ctor", module.TypeSystem.Void, attType_replace) { HasThis = true }));
        }

        static void AdjustInterfaces(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (var interfImpl in type.Interfaces) {
                var old = mappedType.Interfaces.FirstOrDefault(t => t.InterfaceType.FullName == interfImpl.InterfaceType.FullName);
                if (old is not null) {
                    mappedType.Interfaces.Remove(old);
                }
                AdjustMemberAttributes(target, mod, interfImpl.CustomAttributes);
                var mappedInterfType = interfImpl.InterfaceType;
                if (RedirectTypeRef(target, mod, ref mappedInterfType)) {
                    interfImpl.InterfaceType = mappedInterfType;
                }
                mappedType.Interfaces.Add(interfImpl);
            }
        }
        static void AdjustMemberAttributes(ModuleDefinition target, ModuleDefinition mod, Collection<CustomAttribute> attributes) {
            var array = attributes.ToArray();
            attributes.Clear();
            foreach (var attr in array) {
                var mappedAttr = MapCustomAttribute(target, mod, attr);
                if (mappedAttr != null) {
                    attributes.Add(mappedAttr);
                }
            }
        }

        private static CustomAttribute? MapCustomAttribute(ModuleDefinition target, ModuleDefinition mod, CustomAttribute attr) {
            try {
                var mappedAttr = new CustomAttribute(RedirectElementMethodRef(target, mod, attr.Constructor));
                for (int i = 0; i < attr.ConstructorArguments.Count; i++) {
                    var arg = attr.ConstructorArguments[i];
                    var mappedArgType = arg.Type;
                    RedirectTypeRef(target, mod, ref mappedArgType);
                    var argValue = arg.Value;
                    if (arg.Value is TypeReference mappedArgValue) {
                        RedirectTypeRef(target, mod, ref mappedArgValue);
                        argValue = mappedArgValue;
                    }
                    mappedAttr.ConstructorArguments.Add(new CustomAttributeArgument(mappedArgType, argValue));
                }
                for (int i = 0; i < attr.Properties.Count; i++) {
                    var prop = attr.Properties[i];
                    var mappedPropType = prop.Argument.Type;
                    RedirectTypeRef(target, mod, ref mappedPropType);
                    var propValue = prop.Argument.Value;
                    if (prop.Argument.Value is TypeReference mappedPropValue) {
                        RedirectTypeRef(target, mod, ref mappedPropValue);
                        propValue = mappedPropValue;
                    }
                    mappedAttr.Properties.Add(new CustomAttributeNamedArgument(prop.Name, new(mappedPropType, propValue)));
                }
                for (int i = 0; i < attr.Fields.Count; i++) {
                    var field = attr.Fields[i];
                    var mappedFieldType = field.Argument.Type;
                    RedirectTypeRef(target, mod, ref mappedFieldType);
                    var fieldValue = field.Argument.Value;
                    if (field.Argument.Value is TypeReference mappedFieldValue) {
                        RedirectTypeRef(target, mod, ref mappedFieldValue);
                        fieldValue = mappedFieldValue;
                    }
                    mappedAttr.Fields.Add(new CustomAttributeNamedArgument(field.Name, new(mappedFieldType, fieldValue)));
                }
                return mappedAttr;
            }
            catch {
                return null;
            }
        }

        static bool RedirectTypeRef(ModuleDefinition target, ModuleDefinition mod, ref TypeReference reference) {
            bool anyChanged = false;
            if (reference is null) {
                return anyChanged;
            }
            if (reference is GenericParameter genericParameter) {
                if (genericParameter.DeclaringType is not null) {
                    var newDeclaringType = target.GetType(genericParameter.DeclaringType.FullName);
                    if (newDeclaringType is not null) {
                        reference = newDeclaringType.GenericParameters[genericParameter.Position];
                        anyChanged = true;
                    }
                }
                AdjustMemberAttributes(target, mod, genericParameter.CustomAttributes);
                return anyChanged;
            }
            else if (reference is GenericInstanceType genericOrig) {
                for (int i = 0; i < genericOrig.GenericArguments.Count; i++) {
                    var arg = genericOrig.GenericArguments[i];
                    if (RedirectTypeRef(target, mod, ref arg)) {
                        genericOrig.GenericArguments[i] = arg;
                        anyChanged = true;
                    }
                }
                var elementType = genericOrig.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    var genericInstance = new GenericInstanceType(elementType);
                    genericInstance.GenericArguments.AddRange(genericOrig.GenericArguments);
                    reference = genericInstance;
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is ArrayType array) {
                var elementType = array.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new ArrayType(elementType, array.Rank);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is PointerType pointer) {
                var elementType = pointer.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new PointerType(elementType);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is ByReferenceType byReference) {
                var elementType = byReference.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new ByReferenceType(elementType);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is FunctionPointerType function) {
                var returnType = function.ReturnType;
                if (RedirectTypeRef(target, mod, ref returnType)) {
                    function.ReturnType = returnType;
                    anyChanged = true;
                }
                foreach (var param in function.Parameters) {
                    var paramType = param.ParameterType;
                    if (RedirectTypeRef(target, mod, ref paramType)) {
                        param.ParameterType = paramType;
                        anyChanged = true;
                    }
                }
                foreach (var genericParam in function.GenericParameters) {
                    foreach (var constraint in genericParam.Constraints) {
                        var constraintType = constraint.ConstraintType;
                        if (RedirectTypeRef(target, mod, ref constraintType)) {
                            constraint.ConstraintType = constraintType;
                            anyChanged = true;
                        }
                    }
                }
                return anyChanged;
            }
            else if (reference is TypeSpecification spec) {
                var elementType = spec.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    anyChanged = true;
                }
                return anyChanged;
            }

            if (reference.IsNested) {
                var declaringType = reference.DeclaringType;
                if (RedirectTypeRef(target, mod, ref declaringType)) {
                    reference.DeclaringType = declaringType;
                    anyChanged = true;
                }
            }

            if (reference.Scope.Name == mod.Name) {
                anyChanged = true;
            }

            var scope = mod.Name == reference.Scope.Name ? target : reference.Scope;

            if (scope.Name == target.TypeSystem.CoreLibrary.Name) {
                scope = target.TypeSystem.CoreLibrary;
                anyChanged = true;
            }
            else if (scope != target) {
                var assemblyReference = target.AssemblyReferences.FirstOrDefault(ar => ar.Name == scope.Name);
                if (assemblyReference is not null) {
                    scope = assemblyReference;
                    anyChanged = true;
                }
            }

            if (anyChanged) {
                if (reference is TypeDefinition td) {
                    if (td.HasGenericParameters) {
                        reference = target.GetType(td.FullName);
                    }
                    else {
                        var tmp = new TypeReference(reference.Namespace, reference.Name, target, scope, reference.IsValueType) {
                            DeclaringType = reference.DeclaringType
                        };
                        reference = tmp;
                    }
                }
                else {
                    innerField_scope.SetValue(reference, scope);
                    innerField_module.SetValue(reference, target);
                }
            }

            return anyChanged;
        }
        static readonly System.Reflection.BindingFlags innerFieldBindings = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        static readonly System.Reflection.FieldInfo innerField_scope = typeof(TypeReference).GetField("scope", innerFieldBindings)!;
        static readonly System.Reflection.FieldInfo innerField_module = typeof(TypeReference).GetField("module", innerFieldBindings)!;
    }
}
