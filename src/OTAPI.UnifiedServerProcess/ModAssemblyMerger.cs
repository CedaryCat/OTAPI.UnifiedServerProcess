using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using MonoMod.Utils;
using Newtonsoft.Json.Linq;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess
{
    /// <summary>
    /// Configures how source members are overlaid onto an existing target assembly.
    /// Entries in <see cref="IgnoreExistingMethods"/> may be a method name or a
    /// complete identifier produced by <see cref="MonoModExtensions.GetIdentifier(MethodReference, bool)"/>.
    /// </summary>
    public record struct MergeOption(ImmutableArray<string> IgnoreExistingMethods);
    public class ModAssemblyMerger
    {
        private enum MethodMergeAction
        {
            AddSource,
            IgnoreSource,
            ReplaceTarget,
            ComposeStaticConstructor,
        }

        readonly HashSet<string> IgnoreExistingMethods;
        readonly Dictionary<string, ModuleDefinition> modModules = [];
        readonly Dictionary<string, ModuleDefinition> metadataSourceModules = [];
        public ModAssemblyMerger(MergeOption option, params System.Reflection.Assembly[] mods) {

            IgnoreExistingMethods = option.IgnoreExistingMethods.IsDefaultOrEmpty
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(option.IgnoreExistingMethods, StringComparer.Ordinal);

            foreach (System.Reflection.Assembly assembly in mods) {
                var mod = AssemblyDefinition.ReadAssembly(assembly.Location);
                modModules.TryAdd(mod.FullName, mod.MainModule);

                // MonoMod mutates the module passed to PatchModule, including custom-attribute
                // backing blobs. Keep an untouched metadata source for the final interface
                // attribute refresh performed immediately before writing the target assembly.
                var metadataSource = AssemblyDefinition.ReadAssembly(assembly.Location);
                metadataSourceModules.TryAdd(metadataSource.FullName, metadataSource.MainModule);
            }
        }
        public void Attach(ModContext context) {
            context.OnApply += (progress, modder) => {
                if (modder is null) {
                    return ModContext.EApplyResult.Continue;
                }
                ModuleDefinition module = modder.Module;
                if (progress == ModType.PreRead) {
                }
                else if (progress == ModType.PrePatch) {
                    var modderTypes = module.GetAllTypes().ToDictionary(x => x.FullName, x => x);
                    Dictionary<string, TypeDefinition> modsTypes = [];
                    foreach (ModuleDefinition mod in modModules.Values) {
                        foreach (TypeDefinition? type in mod.Types) {
                            SetModTypePlaceholder(module, modderTypes, type, null);
                        }
                        modder.PrePatchModule(mod);
                        modder.PatchModule(mod);
                        modderTypes = module.GetAllTypes().ToDictionary(x => x.FullName, x => x);

                        foreach (TypeDefinition? type in mod.GetAllTypes()) {
                            TypeDefinition mappedType = module.GetType(type.FullName) ?? throw new NotSupportedException();
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

        internal void RefreshMergedInterfaceAttributes(ModuleDefinition target) {
            Dictionary<string, TypeDefinition> targetTypes = target.GetAllTypes()
                .ToDictionary(type => type.FullName, type => type);
            foreach (ModuleDefinition mod in metadataSourceModules.Values) {
                foreach (TypeDefinition type in mod.GetAllTypes()) {
                    if (targetTypes.TryGetValue(type.FullName, out TypeDefinition? mappedType)) {
                        RefreshInterfaceAttributes(target, mod, type, mappedType);
                    }
                }
            }
        }

        private static void RefreshInterfaceAttributes(
            ModuleDefinition target,
            ModuleDefinition mod,
            TypeDefinition type,
            TypeDefinition mappedType) {
            foreach (InterfaceImplementation sourceInterface in type.Interfaces) {
                InterfaceImplementation? mappedInterface = mappedType.Interfaces.FirstOrDefault(
                    candidate => candidate.InterfaceType.FullName == sourceInterface.InterfaceType.FullName);
                if (mappedInterface is null) {
                    continue;
                }
                int mappedInterfaceIndex = mappedType.Interfaces.IndexOf(mappedInterface);

                var mappedAttributes = new List<CustomAttribute>();
                foreach (CustomAttribute sourceAttribute in sourceInterface.CustomAttributes) {
                    MethodReference? existingConstructor = mappedInterface.CustomAttributes
                        .FirstOrDefault(attribute =>
                            attribute.AttributeType.FullName == sourceAttribute.AttributeType.FullName &&
                            attribute.Constructor.FullName == sourceAttribute.Constructor.FullName)
                        ?.Constructor;
                    CustomAttribute? mappedAttribute = MapCustomAttributePreservingBlob(
                        target, mod, sourceAttribute, existingConstructor);
                    if (mappedAttribute is not null) {
                        mappedAttributes.Add(mappedAttribute);
                    }
                }

                TypeReference refreshedInterfaceType = target.ImportReference(
                    mappedType.Interfaces[mappedInterfaceIndex].InterfaceType);
                var refreshedInterface = new InterfaceImplementation(refreshedInterfaceType);
                refreshedInterface.CustomAttributes.AddRange(mappedAttributes);
                mappedType.Interfaces.RemoveAt(mappedInterfaceIndex);
                mappedType.Interfaces.Add(refreshedInterface);
            }
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
                TypeReference baseType = type.BaseType;
                RedirectTypeRef(target, mod, ref baseType);
                mappedType.BaseType = baseType;
            }
            if (type.HasGenericParameters) {
                mappedType.GenericParameters.Clear();
                foreach (GenericParameter? param in type.GenericParameters) {
                    var gp = new GenericParameter(param.Name, mappedType);
                    foreach (GenericParameterConstraint? constraint in param.Constraints) {
                        TypeReference constraintType = constraint.ConstraintType;
                        RedirectTypeRef(target, mod, ref constraintType);
                        gp.Constraints.Add(new GenericParameterConstraint(constraintType));
                    }
                    mappedType.GenericParameters.Add(gp);

                    foreach (CustomAttribute? att in param.CustomAttributes) {
                        CustomAttribute? mapped = MapCustomAttribute(target, mod, att);
                        if (mapped is not null) {
                            gp.CustomAttributes.Add(mapped);
                        }
                    }
                }
            }
            AdjustMemberAttributes(target, mod, mappedType.CustomAttributes);
        }

        private static void AdjustMembers(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (FieldDefinition? field in mappedType.Fields.ToArray()) {
                TypeReference fieldType = field.FieldType;
                if (RedirectTypeRef(target, mod, ref fieldType)) {
                    field.FieldType = fieldType;
                }
                AdjustMemberAttributes(target, mod, field.CustomAttributes);
            }
            foreach (PropertyDefinition? property in mappedType.Properties.ToArray()) {
                TypeReference propertyType = property.PropertyType;
                if (RedirectTypeRef(target, mod, ref propertyType)) {
                    property.PropertyType = propertyType;
                }
                AdjustMemberAttributes(target, mod, property.CustomAttributes);
            }
            foreach (EventDefinition? eventDef in mappedType.Events.ToArray()) {
                TypeReference eventType = eventDef.EventType;
                if (RedirectTypeRef(target, mod, ref eventType)) {
                    eventDef.EventType = eventType;
                }
                AdjustMemberAttributes(target, mod, eventDef.CustomAttributes);
            }
            foreach (MethodDefinition? method in mappedType.Methods.ToArray()) {
                TypeReference methodType = method.ReturnType;
                if (RedirectTypeRef(target, mod, ref methodType)) {
                    method.ReturnType = methodType;
                }
                AdjustMemberAttributes(target, mod, method.MethodReturnType.CustomAttributes);
                foreach (ParameterDefinition? param in method.Parameters) {
                    TypeReference paramType = param.ParameterType;
                    if (RedirectTypeRef(target, mod, ref paramType)) {
                        param.ParameterType = paramType;
                    }
                    AdjustMemberAttributes(target, mod, param.CustomAttributes);
                }
                foreach (GenericParameter? genericParam in method.GenericParameters) {
                    foreach (GenericParameterConstraint? constraint in genericParam.Constraints) {
                        TypeReference constraintType = constraint.ConstraintType;
                        if (RedirectTypeRef(target, mod, ref constraintType)) {
                            constraint.ConstraintType = constraintType;
                        }
                    }
                    AdjustMemberAttributes(target, mod, genericParam.CustomAttributes);
                }
                for (int i = 0; i < method.Overrides.Count; i++) {
                    MethodReference ovrride = method.Overrides[i];
                    if (ovrride is GenericInstanceMethod generic) {
                        MethodReference elementMethod = RedirectElementMethodRef(target, mod, generic.ElementMethod);
                        var mappedGeneric = new GenericInstanceMethod(elementMethod);
                        for (var j = 0; j < generic.GenericArguments.Count; j++) {
                            TypeReference arg = generic.GenericArguments[j];
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
                    foreach (VariableDefinition? local in method.Body.Variables) {
                        TypeReference localType = local.VariableType;
                        if (RedirectTypeRef(target, mod, ref localType)) {
                            local.VariableType = localType;
                        }
                    }
                    foreach (ExceptionHandler? ex in method.Body.ExceptionHandlers) {
                        TypeReference exType = ex.CatchType;
                        if (RedirectTypeRef(target, mod, ref exType)) {
                            ex.CatchType = exType;
                        }
                    }
                }
            }
        }

        static void AdjustInstructions(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (MethodDefinition? method in mappedType.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (Instruction? inst in method.Body.Instructions) {
                    if (inst.Operand is not MemberReference mr) {
                        continue;
                    }
                    if (inst.Operand is FieldReference fieldRef) {
                        TypeReference declaringType = fieldRef.DeclaringType;
                        TypeReference fieldType = fieldRef.FieldType;
                        RedirectTypeRef(target, mod, ref declaringType);
                        RedirectTypeRef(target, mod, ref fieldType);
                        fieldRef = new FieldReference(fieldRef.Name, fieldType, declaringType);
                        inst.Operand = fieldRef;
                    }
                    else if (inst.Operand is MethodReference methodRef) {
                        if (methodRef is GenericInstanceMethod generic) {
                            MethodReference elementMethod = RedirectElementMethodRef(target, mod, generic.ElementMethod);
                            var mappedGeneric = new GenericInstanceMethod(elementMethod);
                            for (var i = 0; i < generic.GenericArguments.Count; i++) {
                                TypeReference arg = generic.GenericArguments[i];
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
                        TypeReference declaringType = propRef.DeclaringType;
                        TypeReference propType = propRef.PropertyType;
                        RedirectTypeRef(target, mod, ref declaringType);
                        RedirectTypeRef(target, mod, ref propType);
                        propRef.DeclaringType = declaringType;
                        propRef.PropertyType = propType;
                    }
                    else if (inst.Operand is EventReference eventRef) {
                        TypeReference declaringType = eventRef.DeclaringType;
                        TypeReference eventType = eventRef.EventType;
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
                        TypeReference returnType = callSite.ReturnType;
                        RedirectTypeRef(target, mod, ref returnType);
                        callSite.ReturnType = returnType;
                        foreach (ParameterDefinition? arg in callSite.Parameters) {
                            TypeReference argType = arg.ParameterType;
                            RedirectTypeRef(target, mod, ref argType);
                            arg.ParameterType = argType;
                        }
                    }
                    else if (inst.Operand is ParameterReference parameter) {
                        TypeReference parameterType = parameter.ParameterType;
                        RedirectTypeRef(target, mod, ref parameterType);
                        parameter.ParameterType = parameterType;
                    }
                    else if (inst.Operand is VariableReference variable) {
                        TypeReference variableType = variable.VariableType;
                        RedirectTypeRef(target, mod, ref variableType);
                        variable.VariableType = variableType;
                    }
                }
            }
        }

        static MethodReference RedirectElementMethodRef(ModuleDefinition target, ModuleDefinition mod, MethodReference methodRef) {
            TypeReference declaringType = methodRef.DeclaringType;
            TypeReference methodType = methodRef.ReturnType;
            RedirectTypeRef(target, mod, ref declaringType);
            RedirectTypeRef(target, mod, ref methodType);
            var mappedMethod = new MethodReference(methodRef.Name, methodType, declaringType) {
                HasThis = methodRef.HasThis
            };
            foreach (ParameterDefinition? param in methodRef.Parameters) {
                TypeReference paramType = param.ParameterType;
                RedirectTypeRef(target, mod, ref paramType);
                mappedMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, paramType));
            }
            if (methodRef.HasGenericParameters) {
                foreach (GenericParameter? genericParam in methodRef.GenericParameters) {
                    var mappedGenericParam = new GenericParameter(mappedMethod);
                    foreach (GenericParameterConstraint? constraint in genericParam.Constraints) {
                        TypeReference constraintType = constraint.ConstraintType;
                        RedirectTypeRef(target, mod, ref constraintType);
                        genericParam.Constraints.Add(new GenericParameterConstraint(constraintType));
                    }
                    mappedMethod.GenericParameters.Add(mappedGenericParam);
                }
            }

            return mappedMethod;
        }
        void SetModTypePlaceholder(ModuleDefinition module, Dictionary<string, TypeDefinition> uspTypes, TypeDefinition modType, TypeDefinition? declaringType) {
            if (!uspTypes.TryGetValue(modType.FullName, out TypeDefinition? target)) {
                target = new TypeDefinition(modType.Namespace, modType.Name, modType.Attributes, modType.BaseType) {
                    Attributes = modType.Attributes,
                };
                if (declaringType is not null) {
                    declaringType.NestedTypes.Add(target);
                }
                else {
                    module.Types.Add(target);
                }
                foreach (MethodDefinition? method in modType.Methods) {
                    ApplyMethodMergePolicy(target, method, null);
                }
            }
            else {
                modType.BaseType = target.BaseType;

                var existingMethods = target.Methods.ToDictionary(m => m.GetIdentifier(), m => m);
                foreach (MethodDefinition? method in modType.Methods) {
                    if (!existingMethods.TryGetValue(method.GetIdentifier(), out MethodDefinition? existingMethod)) {
                        existingMethod = null;
                    }
                    ApplyMethodMergePolicy(target, method, existingMethod);
                }
                if (modType.IsEnum) {
                    SetMemberReplace(modType.Module, modType.CustomAttributes, true);
                }
                foreach (FieldDefinition? field in modType.Fields) {
                    if (modType.IsEnum && !field.IsStatic) {
                        SetMemberReplace(modType.Module, field.CustomAttributes, false);
                    }
                    else {
                        SetMemberReplace(modType.Module, field.CustomAttributes, modType.IsEnum);
                    }
                }
            }
            foreach (TypeDefinition? nested in modType.NestedTypes) {
                SetModTypePlaceholder(module, uspTypes, nested, target);
            }
        }
        void ApplyMethodMergePolicy(TypeDefinition targetType, MethodDefinition sourceMethod, MethodDefinition? targetMethod) {
            MethodMergeAction action = ResolveMethodMergeAction(targetType, sourceMethod, targetMethod);

            switch (action) {
                case MethodMergeAction.IgnoreSource:
                    SetMonoModAttribute(sourceMethod.Module, sourceMethod.CustomAttributes, typeof(MonoMod.MonoModIgnore));
                    break;
                case MethodMergeAction.AddSource:
                    if (sourceMethod.IsConstructor && !sourceMethod.IsStatic) {
                        SetMonoModAttribute(sourceMethod.Module, sourceMethod.CustomAttributes, typeof(MonoMod.MonoModConstructor));
                    }
                    break;
                case MethodMergeAction.ReplaceTarget:
                    if (sourceMethod.IsConstructor && !sourceMethod.IsStatic) {
                        SetMonoModAttribute(sourceMethod.Module, sourceMethod.CustomAttributes, typeof(MonoMod.MonoModConstructor));
                    }
                    PreserveTargetVirtualContract(sourceMethod, targetMethod!);
                    SetMemberReplace(sourceMethod.Module, sourceMethod.CustomAttributes, false);
                    break;
                case MethodMergeAction.ComposeStaticConstructor:
                    // MonoMod composes a source .cctor with the existing target .cctor by
                    // retaining and calling the old body. MonoModReplace would discard it.
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        MethodMergeAction ResolveMethodMergeAction(
            TypeDefinition targetType,
            MethodDefinition sourceMethod,
            MethodDefinition? targetMethod) {

            if (targetMethod is not null && ShouldIgnoreExistingMethod(sourceMethod)) {
                return MethodMergeAction.IgnoreSource;
            }

            if (sourceMethod.IsConstructor && !sourceMethod.IsStatic && IsTrivialInstanceConstructor(sourceMethod)) {
                if (targetMethod is not null || ShouldSuppressSyntheticDefaultConstructor(targetType, sourceMethod)) {
                    return MethodMergeAction.IgnoreSource;
                }
            }

            if (targetMethod is null) {
                return MethodMergeAction.AddSource;
            }

            if (sourceMethod.IsConstructor && sourceMethod.IsStatic) {
                return MethodMergeAction.ComposeStaticConstructor;
            }

            // Accessors, event methods, operators and instance constructors are all
            // replaceable methods in MonoMod. IsSpecialName does not mean "preserve";
            // omitting MonoModReplace merely creates an orig_* clone before replacement.
            return MethodMergeAction.ReplaceTarget;
        }

        static void PreserveTargetVirtualContract(MethodDefinition sourceMethod, MethodDefinition targetMethod) {
            if (!targetMethod.IsVirtual) {
                return;
            }

            // Replacing a method body must not silently rewrite the target assembly's vtable.
            // This is especially important for implicit interface implementations on value
            // types, which the CLR represents as virtual + newslot + final methods even when
            // the equivalent standalone source method is not declared virtual in C#.
            const MethodAttributes virtualContractMask =
                MethodAttributes.MemberAccessMask
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.VtableLayoutMask
                | MethodAttributes.CheckAccessOnOverride;
            const MethodAttributes identityMask =
                MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName;

            sourceMethod.Attributes =
                (sourceMethod.Attributes & ~virtualContractMask)
                | (targetMethod.Attributes & virtualContractMask)
                | (targetMethod.Attributes & identityMask);
        }

        bool ShouldIgnoreExistingMethod(MethodDefinition method) {
            return IgnoreExistingMethods.Contains(method.Name)
                || IgnoreExistingMethods.Contains(method.GetIdentifier(withDeclaring: false))
                || IgnoreExistingMethods.Contains(method.GetIdentifier());
        }

        static bool ShouldSuppressSyntheticDefaultConstructor(TypeDefinition targetType, MethodDefinition sourceMethod) {
            return sourceMethod.Parameters.Count is 0
                && !targetType.Methods.Any(m => m is { IsConstructor: true, IsStatic: false, Parameters: [] })
                && targetType.Methods.Any(m => m.IsConstructor && !m.IsStatic);
        }

        static bool IsTrivialInstanceConstructor(MethodDefinition method) {
            if (!method.IsConstructor || method.IsStatic || !method.HasBody) {
                return false;
            }

            Instruction[] instructions = method.Body.Instructions
                .Where(instruction => instruction.OpCode != OpCodes.Nop)
                .ToArray();

            // An explicitly empty value-type constructor can consist solely of ret.
            if (instructions is [{ OpCode: var onlyOpCode }]
                && onlyOpCode == OpCodes.Ret
                && method.DeclaringType.IsValueType) {
                return true;
            }

            if (instructions.Length != 3
                || instructions[0].OpCode != OpCodes.Ldarg_0
                || instructions[2].OpCode != OpCodes.Ret) {
                return false;
            }

            Instruction initializer = instructions[1];
            if (initializer.OpCode == OpCodes.Call
                && initializer.Operand is MethodReference calledConstructor
                && calledConstructor.Name == ".ctor") {
                // A this(...) constructor delegates real work and must not be treated as empty.
                return calledConstructor.DeclaringType.FullName != method.DeclaringType.FullName;
            }

            return method.DeclaringType.IsValueType
                && initializer.OpCode == OpCodes.Initobj
                && initializer.Operand is TypeReference initializedType
                && initializedType.FullName == method.DeclaringType.FullName;
        }
        static void SetMemberReplace(ModuleDefinition module, Collection<CustomAttribute> attributes, bool isEnum) {
            Type type = isEnum ? typeof(MonoMod.MonoModEnumReplace) : typeof(MonoMod.MonoModReplace);
            SetMonoModAttribute(module, attributes, type);
        }

        static void SetMonoModAttribute(ModuleDefinition module, Collection<CustomAttribute> attributes, Type type) {
            if (attributes.Any(attribute => attribute.AttributeType.FullName == type.FullName)) {
                return;
            }
            TypeReference attributeType = module.ImportReference(type);
            attributes.Add(new CustomAttribute(new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true }));
        }

        static void AdjustInterfaces(ModuleDefinition target, ModuleDefinition mod, TypeDefinition type, TypeDefinition mappedType) {
            foreach (InterfaceImplementation? interfImpl in type.Interfaces) {
                InterfaceImplementation? old = mappedType.Interfaces.FirstOrDefault(t => t.InterfaceType.FullName == interfImpl.InterfaceType.FullName);
                if (old is not null) {
                    mappedType.Interfaces.Remove(old);
                }
                TypeReference mappedInterfType = interfImpl.InterfaceType;
                RedirectTypeRef(target, mod, ref mappedInterfType);

                var mappedInterface = new InterfaceImplementation(mappedInterfType);
                foreach (CustomAttribute attribute in interfImpl.CustomAttributes) {
                    CustomAttribute? mappedAttribute = MapCustomAttribute(target, mod, attribute);
                    if (mappedAttribute is not null) {
                        mappedInterface.CustomAttributes.Add(mappedAttribute);
                    }
                }
                mappedType.Interfaces.Add(mappedInterface);
            }
        }
        static void AdjustMemberAttributes(ModuleDefinition target, ModuleDefinition mod, Collection<CustomAttribute> attributes) {
            CustomAttribute[] array = attributes.ToArray();
            attributes.Clear();
            foreach (CustomAttribute attr in array) {
                CustomAttribute? mappedAttr = MapCustomAttribute(target, mod, attr);
                if (mappedAttr != null) {
                    attributes.Add(mappedAttr);
                }
            }
        }

        private static CustomAttribute? MapCustomAttribute(ModuleDefinition target, ModuleDefinition mod, CustomAttribute attr) {
            try {
                MethodReference mappedConstructor = RedirectElementMethodRef(target, mod, attr.Constructor);
                var mappedAttr = new CustomAttribute(target.ImportReference(mappedConstructor));
                for (int i = 0; i < attr.ConstructorArguments.Count; i++) {
                    CustomAttributeArgument arg = attr.ConstructorArguments[i];
                    TypeReference mappedArgType = arg.Type;
                    RedirectTypeRef(target, mod, ref mappedArgType);
                    var argValue = arg.Value;
                    if (arg.Value is TypeReference mappedArgValue) {
                        RedirectTypeRef(target, mod, ref mappedArgValue);
                        argValue = mappedArgValue;
                    }
                    mappedAttr.ConstructorArguments.Add(new CustomAttributeArgument(mappedArgType, argValue));
                }
                for (int i = 0; i < attr.Properties.Count; i++) {
                    CustomAttributeNamedArgument prop = attr.Properties[i];
                    TypeReference mappedPropType = prop.Argument.Type;
                    RedirectTypeRef(target, mod, ref mappedPropType);
                    var propValue = prop.Argument.Value;
                    if (prop.Argument.Value is TypeReference mappedPropValue) {
                        RedirectTypeRef(target, mod, ref mappedPropValue);
                        propValue = mappedPropValue;
                    }
                    mappedAttr.Properties.Add(new CustomAttributeNamedArgument(prop.Name, new(mappedPropType, propValue)));
                }
                for (int i = 0; i < attr.Fields.Count; i++) {
                    CustomAttributeNamedArgument field = attr.Fields[i];
                    TypeReference mappedFieldType = field.Argument.Type;
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

        private static CustomAttribute? MapCustomAttributePreservingBlob(
            ModuleDefinition target,
            ModuleDefinition mod,
            CustomAttribute attr,
            MethodReference? existingTargetConstructor = null) {
            try {
                byte[] blob = attr.GetBlob().ToArray();
                MethodReference mappedConstructor = existingTargetConstructor is not null
                    ? existingTargetConstructor
                    : RedirectElementMethodRef(target, mod, attr.Constructor);
                return new CustomAttribute(target.ImportReference(mappedConstructor), blob);
            }
            catch (NotSupportedException) {
                return MapCustomAttribute(target, mod, attr);
            }
        }

        static bool RedirectTypeRef(ModuleDefinition target, ModuleDefinition mod, ref TypeReference reference) {
            bool anyChanged = false;
            if (reference is null) {
                return anyChanged;
            }
            if (reference is GenericParameter genericParameter) {
                if (genericParameter.DeclaringType is not null) {
                    TypeDefinition? newDeclaringType = target.GetType(genericParameter.DeclaringType.FullName);
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
                    TypeReference arg = genericOrig.GenericArguments[i];
                    if (RedirectTypeRef(target, mod, ref arg)) {
                        genericOrig.GenericArguments[i] = arg;
                        anyChanged = true;
                    }
                }
                TypeReference elementType = genericOrig.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    var genericInstance = new GenericInstanceType(elementType);
                    genericInstance.GenericArguments.AddRange(genericOrig.GenericArguments);
                    reference = genericInstance;
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is ArrayType array) {
                TypeReference elementType = array.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new ArrayType(elementType, array.Rank);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is PointerType pointer) {
                TypeReference elementType = pointer.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new PointerType(elementType);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is ByReferenceType byReference) {
                TypeReference elementType = byReference.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    reference = new ByReferenceType(elementType);
                    anyChanged = true;
                }
                return anyChanged;
            }
            else if (reference is FunctionPointerType function) {
                TypeReference returnType = function.ReturnType;
                if (RedirectTypeRef(target, mod, ref returnType)) {
                    function.ReturnType = returnType;
                    anyChanged = true;
                }
                foreach (ParameterDefinition? param in function.Parameters) {
                    TypeReference paramType = param.ParameterType;
                    if (RedirectTypeRef(target, mod, ref paramType)) {
                        param.ParameterType = paramType;
                        anyChanged = true;
                    }
                }
                foreach (GenericParameter? genericParam in function.GenericParameters) {
                    foreach (GenericParameterConstraint? constraint in genericParam.Constraints) {
                        TypeReference constraintType = constraint.ConstraintType;
                        if (RedirectTypeRef(target, mod, ref constraintType)) {
                            constraint.ConstraintType = constraintType;
                            anyChanged = true;
                        }
                    }
                }
                return anyChanged;
            }
            else if (reference is TypeSpecification spec) {
                TypeReference elementType = spec.ElementType;
                if (RedirectTypeRef(target, mod, ref elementType)) {
                    anyChanged = true;
                }
                return anyChanged;
            }

            if (reference.IsNested) {
                TypeReference declaringType = reference.DeclaringType;
                if (RedirectTypeRef(target, mod, ref declaringType)) {
                    reference.DeclaringType = declaringType;
                    anyChanged = true;
                }
            }

            if (reference.Scope.Name == mod.Name) {
                anyChanged = true;
            }

            IMetadataScope scope = mod.Name == reference.Scope.Name ? target : reference.Scope;

            if (scope.Name == target.TypeSystem.CoreLibrary.Name) {
                scope = target.TypeSystem.CoreLibrary;
                anyChanged = true;
            }
            else if (scope != target) {
                AssemblyNameReference? assemblyReference = target.AssemblyReferences.FirstOrDefault(ar => ar.Name == scope.Name);
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
