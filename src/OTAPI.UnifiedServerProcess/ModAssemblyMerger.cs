using ModFramework;
using Mono.Cecil;
using ModFramework.Relinker;
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

        private sealed record InterfaceAttributeMetadataSnapshot(
            string AttributeTypeFullName,
            string ConstructorFullName,
            MethodReference Constructor,
            ImmutableArray<byte> Blob);

        private sealed record InterfaceMetadataSnapshot(
            string InterfaceTypeFullName,
            ImmutableArray<InterfaceAttributeMetadataSnapshot> Attributes);

        private sealed record TypeInterfaceMetadataSnapshot(
            ModuleDefinition SourceModule,
            string TypeFullName,
            ImmutableArray<InterfaceMetadataSnapshot> Interfaces);

        readonly HashSet<string> IgnoreExistingMethods;
        readonly Dictionary<string, ModuleDefinition> modModules = [];
        readonly ImmutableArray<TypeInterfaceMetadataSnapshot> interfaceMetadataSnapshots;
        public ModAssemblyMerger(MergeOption option, params System.Reflection.Assembly[] mods) {

            IgnoreExistingMethods = option.IgnoreExistingMethods.IsDefaultOrEmpty
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(option.IgnoreExistingMethods, StringComparer.Ordinal);

            var metadataSnapshots = ImmutableArray.CreateBuilder<TypeInterfaceMetadataSnapshot>();

            foreach (System.Reflection.Assembly assembly in mods) {
                var mod = AssemblyDefinition.ReadAssembly(assembly.Location);
                modModules.TryAdd(mod.FullName, mod.MainModule);

                // A second deferred Cecil graph is not an immutable metadata snapshot: a
                // CustomAttribute reads its blob through Constructor.Module, and constructor
                // references are shared between attributes. Capture every interface attribute
                // blob before MonoMod or any reference mapper can touch either source graph.
                var metadataSource = AssemblyDefinition.ReadAssembly(
                    assembly.Location,
                    new ReaderParameters(ReadingMode.Deferred) { InMemory = true });
                SnapshotInterfaceMetadata(metadataSource.MainModule, metadataSnapshots);
            }

            interfaceMetadataSnapshots = metadataSnapshots.ToImmutable();
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
            foreach (TypeInterfaceMetadataSnapshot type in interfaceMetadataSnapshots) {
                if (targetTypes.TryGetValue(type.TypeFullName, out TypeDefinition? mappedType)) {
                    RefreshInterfaceAttributes(target, type, mappedType);
                }
            }
        }

        internal RelinkTask CreateInterfaceMetadataRefreshTask(ModFwModder modder) {
            return new InterfaceMetadataRefreshTask(modder, this);
        }

        private sealed class InterfaceMetadataRefreshTask(
            ModFwModder modder,
            ModAssemblyMerger merger) : RelinkTask(modder)
        {
            // ModFwModder executes tasks in ascending order immediately before the Cecil
            // writer. Keep the metadata restore after the framework's relink tasks.
            public override int Order { get; set; } = 1_000_000;

            public override void PreWrite() {
                merger.RefreshMergedInterfaceAttributes(Modder.Module);
            }
        }

        private static void RefreshInterfaceAttributes(
            ModuleDefinition target,
            TypeInterfaceMetadataSnapshot type,
            TypeDefinition mappedType) {
            foreach (InterfaceMetadataSnapshot sourceInterface in type.Interfaces) {
                InterfaceImplementation? mappedInterface = mappedType.Interfaces.FirstOrDefault(
                    candidate => candidate.InterfaceType.FullName == sourceInterface.InterfaceTypeFullName);
                if (mappedInterface is null) {
                    continue;
                }
                int mappedInterfaceIndex = mappedType.Interfaces.IndexOf(mappedInterface);

                var mappedAttributes = new List<CustomAttribute>();
                foreach (InterfaceAttributeMetadataSnapshot sourceAttribute in sourceInterface.Attributes) {
                    MethodReference? existingConstructor = mappedInterface.CustomAttributes
                        .FirstOrDefault(attribute =>
                            attribute.AttributeType.FullName == sourceAttribute.AttributeTypeFullName &&
                            attribute.Constructor.FullName == sourceAttribute.ConstructorFullName)
                        ?.Constructor;
                    mappedAttributes.Add(MapCustomAttributeSnapshot(
                        target, type.SourceModule, sourceAttribute, existingConstructor));
                }

                TypeReference refreshedInterfaceType = target.ImportReference(
                    mappedType.Interfaces[mappedInterfaceIndex].InterfaceType);
                var refreshedInterface = new InterfaceImplementation(refreshedInterfaceType);
                refreshedInterface.CustomAttributes.AddRange(mappedAttributes);
                mappedType.Interfaces.RemoveAt(mappedInterfaceIndex);
                mappedType.Interfaces.Add(refreshedInterface);
            }
        }

        private static void SnapshotInterfaceMetadata(
            ModuleDefinition source,
            ImmutableArray<TypeInterfaceMetadataSnapshot>.Builder snapshots) {
            foreach (TypeDefinition type in source.GetAllTypes()) {
                if (!type.HasInterfaces) {
                    continue;
                }

                var interfaces = ImmutableArray.CreateBuilder<InterfaceMetadataSnapshot>(type.Interfaces.Count);
                foreach (InterfaceImplementation implementation in type.Interfaces) {
                    var attributes = ImmutableArray.CreateBuilder<InterfaceAttributeMetadataSnapshot>(
                        implementation.CustomAttributes.Count);
                    foreach (CustomAttribute attribute in implementation.CustomAttributes) {
                        byte[] blob = attribute.GetBlob().ToArray();
                        if (blob.Length < 2 || blob[0] != 0x01 || blob[1] != 0x00) {
                            throw new BadImageFormatException(
                                $"Interface custom attribute {attribute.AttributeType.FullName} on " +
                                $"{type.FullName} has an invalid prolog.");
                        }

                        attributes.Add(new InterfaceAttributeMetadataSnapshot(
                            attribute.AttributeType.FullName,
                            attribute.Constructor.FullName,
                            attribute.Constructor,
                            ImmutableArray.CreateRange(blob)));
                    }

                    interfaces.Add(new InterfaceMetadataSnapshot(
                        implementation.InterfaceType.FullName,
                        attributes.ToImmutable()));
                }

                snapshots.Add(new TypeInterfaceMetadataSnapshot(
                    source,
                    type.FullName,
                    interfaces.ToImmutable()));
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
                HasThis = methodRef.HasThis,
                ExplicitThis = methodRef.ExplicitThis,
                CallingConvention = methodRef.CallingConvention,
            };
            foreach (ParameterDefinition? param in methodRef.Parameters) {
                TypeReference paramType = param.ParameterType;
                RedirectTypeRef(target, mod, ref paramType);
                mappedMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, paramType));
            }
            if (methodRef.HasGenericParameters) {
                foreach (GenericParameter? genericParam in methodRef.GenericParameters) {
                    var mappedGenericParam = new GenericParameter(genericParam.Name, mappedMethod) {
                        Attributes = genericParam.Attributes,
                    };
                    foreach (GenericParameterConstraint? constraint in genericParam.Constraints) {
                        TypeReference constraintType = constraint.ConstraintType;
                        RedirectTypeRef(target, mod, ref constraintType);
                        mappedGenericParam.Constraints.Add(new GenericParameterConstraint(constraintType));
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

        private static CustomAttribute MapCustomAttributeSnapshot(
            ModuleDefinition target,
            ModuleDefinition source,
            InterfaceAttributeMetadataSnapshot attribute,
            MethodReference? existingTargetConstructor = null) {
            MethodReference mappedConstructor;
            if (existingTargetConstructor is not null) {
                mappedConstructor = existingTargetConstructor;
            }
            else {
                ModuleDefinition? constructorModule = attribute.Constructor.Module;
                mappedConstructor = RedirectElementMethodRef(target, source, attribute.Constructor);
                if (!ReferenceEquals(attribute.Constructor.Module, constructorModule)) {
                    throw new InvalidOperationException(
                        $"Mapping custom attribute {attribute.AttributeTypeFullName} mutated its source constructor module.");
                }
            }

            return new CustomAttribute(
                target.ImportReference(mappedConstructor),
                attribute.Blob.ToArray());
        }

        static bool RedirectTypeRef(ModuleDefinition target, ModuleDefinition mod, ref TypeReference reference) {
            if (reference is null) {
                return false;
            }

            TypeReference mapped = MapTypeReferenceWithoutMutatingSource(target, mod, reference);
            if (ReferenceEquals(mapped, reference)) {
                return false;
            }

            reference = mapped;
            return true;
        }

        private static TypeReference MapTypeReferenceWithoutMutatingSource(
            ModuleDefinition target,
            ModuleDefinition source,
            TypeReference reference) {
            if (reference is GenericParameter genericParameter) {
                if (genericParameter.DeclaringType is not null) {
                    TypeDefinition? declaringType = target.GetType(genericParameter.DeclaringType.FullName);
                    if (declaringType is not null && genericParameter.Position < declaringType.GenericParameters.Count) {
                        return declaringType.GenericParameters[genericParameter.Position];
                    }
                }
                return genericParameter;
            }

            if (reference is GenericInstanceType genericInstance) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(
                    target, source, genericInstance.ElementType);
                var arguments = new TypeReference[genericInstance.GenericArguments.Count];
                bool changed = !ReferenceEquals(elementType, genericInstance.ElementType);
                for (int i = 0; i < arguments.Length; i++) {
                    TypeReference original = genericInstance.GenericArguments[i];
                    arguments[i] = MapTypeReferenceWithoutMutatingSource(target, source, original);
                    changed |= !ReferenceEquals(arguments[i], original);
                }
                if (!changed) {
                    return genericInstance;
                }

                var mapped = new GenericInstanceType(elementType);
                mapped.GenericArguments.AddRange(arguments);
                return mapped;
            }

            if (reference is ArrayType array) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(target, source, array.ElementType);
                if (ReferenceEquals(elementType, array.ElementType)) {
                    return array;
                }

                var mapped = new ArrayType(elementType);
                if (!array.IsVector) {
                    mapped.Dimensions.Clear();
                    foreach (ArrayDimension dimension in array.Dimensions) {
                        mapped.Dimensions.Add(new ArrayDimension(dimension.LowerBound, dimension.UpperBound));
                    }
                }
                return mapped;
            }

            if (reference is PointerType pointer) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(target, source, pointer.ElementType);
                return ReferenceEquals(elementType, pointer.ElementType)
                    ? pointer
                    : new PointerType(elementType);
            }

            if (reference is ByReferenceType byReference) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(target, source, byReference.ElementType);
                return ReferenceEquals(elementType, byReference.ElementType)
                    ? byReference
                    : new ByReferenceType(elementType);
            }

            if (reference is PinnedType pinned) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(target, source, pinned.ElementType);
                return ReferenceEquals(elementType, pinned.ElementType)
                    ? pinned
                    : new PinnedType(elementType);
            }

            if (reference is SentinelType sentinel) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(target, source, sentinel.ElementType);
                return ReferenceEquals(elementType, sentinel.ElementType)
                    ? sentinel
                    : new SentinelType(elementType);
            }

            if (reference is OptionalModifierType optionalModifier) {
                TypeReference modifierType = MapTypeReferenceWithoutMutatingSource(
                    target, source, optionalModifier.ModifierType);
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(
                    target, source, optionalModifier.ElementType);
                return ReferenceEquals(modifierType, optionalModifier.ModifierType)
                    && ReferenceEquals(elementType, optionalModifier.ElementType)
                    ? optionalModifier
                    : new OptionalModifierType(modifierType, elementType);
            }

            if (reference is RequiredModifierType requiredModifier) {
                TypeReference modifierType = MapTypeReferenceWithoutMutatingSource(
                    target, source, requiredModifier.ModifierType);
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(
                    target, source, requiredModifier.ElementType);
                return ReferenceEquals(modifierType, requiredModifier.ModifierType)
                    && ReferenceEquals(elementType, requiredModifier.ElementType)
                    ? requiredModifier
                    : new RequiredModifierType(modifierType, elementType);
            }

            if (reference is FunctionPointerType function) {
                TypeReference returnType = MapTypeReferenceWithoutMutatingSource(
                    target, source, function.ReturnType);
                var parameterTypes = new TypeReference[function.Parameters.Count];
                bool changed = !ReferenceEquals(returnType, function.ReturnType);
                for (int i = 0; i < parameterTypes.Length; i++) {
                    TypeReference original = function.Parameters[i].ParameterType;
                    parameterTypes[i] = MapTypeReferenceWithoutMutatingSource(target, source, original);
                    changed |= !ReferenceEquals(parameterTypes[i], original);
                }
                if (!changed) {
                    return function;
                }
                if (function.HasGenericParameters) {
                    throw new NotSupportedException(
                        "Cannot non-destructively remap a generic function-pointer signature.");
                }

                var mapped = new FunctionPointerType {
                    HasThis = function.HasThis,
                    ExplicitThis = function.ExplicitThis,
                    CallingConvention = function.CallingConvention,
                    ReturnType = returnType,
                };
                for (int i = 0; i < parameterTypes.Length; i++) {
                    ParameterDefinition original = function.Parameters[i];
                    mapped.Parameters.Add(new ParameterDefinition(
                        original.Name, original.Attributes, parameterTypes[i]));
                }
                return mapped;
            }

            if (reference is TypeSpecification specification) {
                TypeReference elementType = MapTypeReferenceWithoutMutatingSource(
                    target, source, specification.ElementType);
                if (ReferenceEquals(elementType, specification.ElementType)) {
                    return specification;
                }
                throw new NotSupportedException(
                    $"Unsupported type specification {reference.GetType().FullName}.");
            }

            TypeReference? mappedDeclaringType = reference.DeclaringType is null
                ? null
                : MapTypeReferenceWithoutMutatingSource(target, source, reference.DeclaringType);

            if (IsOwnedBySourceModule(reference, source)) {
                TypeDefinition? mappedDefinition = target.GetType(reference.FullName);
                if (mappedDefinition is not null) {
                    return mappedDefinition;
                }

                var mapped = new TypeReference(
                    reference.Namespace,
                    reference.Name,
                    target,
                    target,
                    reference.IsValueType) {
                    DeclaringType = mappedDeclaringType,
                };
                foreach (GenericParameter parameter in reference.GenericParameters) {
                    mapped.GenericParameters.Add(new GenericParameter(parameter.Name, mapped) {
                        Attributes = parameter.Attributes,
                    });
                }
                return mapped;
            }

            if (reference.Module == target
                && ReferenceEquals(mappedDeclaringType, reference.DeclaringType)) {
                return reference;
            }

            TypeReference imported = target.ImportReference(reference);
            if (mappedDeclaringType is not null) {
                imported.DeclaringType = mappedDeclaringType;
            }
            return imported;
        }

        private static bool IsOwnedBySourceModule(
            TypeReference reference,
            ModuleDefinition source) {
            if (reference is TypeDefinition definition) {
                return definition.Module == source;
            }

            IMetadataScope scope = reference.Scope;
            if (ReferenceEquals(scope, source)) {
                return true;
            }
            if (scope is ModuleDefinition module) {
                return module.Name == source.Name
                    && module.Assembly?.Name.FullName == source.Assembly?.Name.FullName;
            }
            if (scope is AssemblyNameReference assembly && source.Assembly is not null) {
                return assembly.Name == source.Assembly.Name.Name;
            }
            return false;
        }
    }
}
