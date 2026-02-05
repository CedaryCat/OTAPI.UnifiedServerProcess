using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.DataModels
{
    public class ContextTypeData
    {
        public readonly TypeDefinition originalType;
        public readonly TypeDefinition ContextTypeDef;
        public readonly MethodDefinition constructor;
        public readonly bool IsReusedSingleton;
        public ImmutableDictionary<string, MethodDefinition> ReusedSingletonMethods;
        public ImmutableDictionary<string, FieldDefinition> ReusedSingletonFields;
        /// <summary>
        /// If <see cref="IsReusedSingleton"/> is true, this is the vanilla singleton field.
        /// <para>And in the subsequent patching process, the vanilla reference to it will be redirected to the context field under the root context.</para>
        /// </summary>
        public readonly FieldDefinition? VanillaSingletonField;
        public bool SingletonCtorCallShouldBeMoveToRootCtor;
        /// <summary>
        /// Before call to the base instance constructor. (is 'this' load operation)
        /// <para>Use <see cref="MonoModExtensions.InsertBeforeSeamlessly(ILProcessor, ref Instruction, IEnumerable{Instruction})"/> or other overloads to insert field init before this.</para>
        /// </summary>
        public Instruction FieldInitInsertionPoint;
        /// <summary>        
        /// When converting, we often want to merge static constructor into instance constructor.
        /// <para>If it just a field init and no reference any parameters or fields, then use <see cref="FieldInitInsertionPoint"/>.</para>
        /// <para>otherwise, use this insertion point, which is after root context assignment and before original instance constructor logic. (is first instruction of the original instance constructor)</para>
        /// <para>Use <see cref="MonoModExtensions.InsertBeforeSeamlessly(ILProcessor, ref Instruction, IEnumerable{Instruction})"/> or other overloads.</para>
        /// </summary>
        public Instruction StaticConstructorBodyInsertionPoint;
        /// <summary>
        /// From root context object to the instance-converted type field.
        /// </summary>
        public readonly FieldDefinition[] nestedChain;
        /// <summary>
        /// The root context field in the instance-converted type.
        /// </summary>
        public readonly FieldDefinition rootContextField;

        public readonly bool IsPredefined;
        public readonly ImmutableDictionary<string, MethodDefinition> PredefinedMethodMap;

        private ContextTypeData(TypeDefinition originalType, TypeDefinition convertedType, FieldDefinition[] nestedChain) {
            this.originalType = originalType;
            this.ContextTypeDef = convertedType;
            this.constructor = convertedType.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            this.IsReusedSingleton = false;
            this.ReusedSingletonMethods = ImmutableDictionary<string, MethodDefinition>.Empty;
            this.ReusedSingletonFields = ImmutableDictionary<string, FieldDefinition>.Empty;
            this.nestedChain = nestedChain;
            this.rootContextField = convertedType.Fields.Single(f => f.Name == Constants.RootContextFieldName);
            this.FieldInitInsertionPoint = constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == ".ctor").Previous;
            this.StaticConstructorBodyInsertionPoint = constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).FullName == rootContextField.FullName).Next;
            IsPredefined = true;
            var predefinedMethodMap = new Dictionary<string, MethodDefinition>();
            var correspondMethod = originalType.Methods.ToDictionary(m => m.GetIdentifier(false), m => m);
            foreach (var method in convertedType.Methods) {
                if (!correspondMethod.TryGetValue(method.GetIdentifier(false), out var originalMethod)) {
                    continue;
                }
                predefinedMethodMap.Add(originalMethod.GetIdentifier(), method);
            }
            PredefinedMethodMap = predefinedMethodMap.ToImmutableDictionary();
        }
        public static ContextTypeData Predefine(TypeDefinition originalType, TypeDefinition convertedType, FieldDefinition[] nestedChain) {
            return new ContextTypeData(originalType, convertedType, nestedChain);
        }

        /// <summary>
        /// Create an instance-converted type from a existing static-field-modified type
        /// <para>If originalType is a singleton, it will be reused.</para>
        /// </summary>
        /// <param name="originalType">create from</param>
        /// <param name="rootContextDef">root context</param>
        /// <param name="instanceConvdTypeOrigMap">auto add self to database</param>
        /// <exception cref="Exception"></exception>
        public ContextTypeData(
            TypeDefinition originalType,
            TypeDefinition rootContextDef,
            IReadOnlyDictionary<string, MethodCallData> callGraph,
            ref Dictionary<string, ContextTypeData> instanceConvdTypeOrigMap) {

            if (originalType.Name == "LocalizedText" || originalType.Name == "Lang") {

            }

            PredefinedMethodMap = ImmutableDictionary<string, MethodDefinition>.Empty;

            if (instanceConvdTypeOrigMap.ContainsKey(originalType.FullName)) {
                throw new Exception("Duplicate contextType type");
            }

            this.originalType = originalType;

            List<TypeDefinition> chain = [];
            var currentType = originalType.DeclaringType;
            while (currentType != null) {
                chain.Add(currentType);
                currentType = currentType.DeclaringType?.TryResolve();
            }
            chain.Reverse();

            foreach (var type in chain) {
                if (!instanceConvdTypeOrigMap.TryGetValue(type.FullName, out var contextType)) {
                    contextType = new ContextTypeData(type, rootContextDef, callGraph, ref instanceConvdTypeOrigMap);
                }
            }

            ContextTypeData? declaringType = null;
            if (chain.Count > 0) {
                declaringType = instanceConvdTypeOrigMap[chain.Last().FullName];
            }

            ContextTypeDef = CreateInstanceConvdTypeOrReuseSingleton(originalType, rootContextDef, declaringType, callGraph,
                out IsReusedSingleton, out rootContextField, out constructor, out VanillaSingletonField, out ReusedSingletonMethods, out ReusedSingletonFields,
                out var storeSelfPlaceHolder);

            // load 'this'
            FieldInitInsertionPoint = constructor.Body.Instructions
                .Single(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == ".ctor")
                .Previous;
            // after root context assignment
            StaticConstructorBodyInsertionPoint = constructor.Body.Instructions
                .First(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == Constants.RootContextFieldName)
                .Next;

            FieldDefinition instanceField;
            if (declaringType is not null) {
                instanceField = CreateContextField(rootContextDef, declaringType, originalType, ContextTypeDef, IsReusedSingleton);
                nestedChain = [.. declaringType.nestedChain, instanceField];
            }
            else {
                instanceField = CreateContextField(rootContextDef, null, originalType, ContextTypeDef, IsReusedSingleton);
                nestedChain = [instanceField];
            }

            storeSelfPlaceHolder.OpCode = OpCodes.Stfld;
            storeSelfPlaceHolder.Operand = instanceField;

            instanceConvdTypeOrigMap.Add(originalType.FullName, this);
        }
        static TypeDefinition CreateInstanceConvdTypeOrReuseSingleton(
            TypeDefinition originalType,
            TypeDefinition contextDef,
            ContextTypeData? declaringType,
            IReadOnlyDictionary<string, MethodCallData> callGraph,
            out bool IsReusedSingleton,
            out FieldDefinition rootContextField,
            out MethodDefinition constructor,
            out FieldDefinition? vanillaSingletonField,
            out ImmutableDictionary<string, MethodDefinition> singletonMathodMap,
            out ImmutableDictionary<string, FieldDefinition> singletonFieldMap,
            out Instruction storeSelfPlaceHolder) {

            if (!CheckIsReusableSingleton(originalType, callGraph, out constructor!, out vanillaSingletonField)) {
                IsReusedSingleton = false;
                singletonMathodMap = ImmutableDictionary<string, MethodDefinition>.Empty;
                singletonFieldMap = ImmutableDictionary<string, FieldDefinition>.Empty;
                return CreateInstanceConvdType(originalType, contextDef, declaringType, out rootContextField, out constructor, out storeSelfPlaceHolder);
            }
            IsReusedSingleton = true;
            var tmpSingletonMathodMap = new Dictionary<string, MethodDefinition>();
            var tmpSingletonFieldMap = new Dictionary<string, FieldDefinition>();

            var reusedType = originalType;
            constructor = reusedType.Methods.Single(m => m.IsConstructor && !m.IsStatic);

            foreach (var method in originalType.Methods.ToArray()) {
                if (!method.IsStatic && !method.IsConstructor) {
                    tmpSingletonMathodMap.Add(method.GetIdentifier(), method);
                }
            }
            foreach (var field in originalType.Fields.ToArray()) {
                if (!field.IsStatic) {
                    tmpSingletonFieldMap.Add(field.GetIdentifier(), field);
                }
            }

            rootContextField = new FieldDefinition(Constants.RootContextFieldName, Constants.Modifiers.RootContextField, contextDef);
            reusedType.Fields.Add(rootContextField);
            ReuseSingletonConstructor(reusedType, constructor, rootContextField, declaringType, out storeSelfPlaceHolder);

            singletonMathodMap = tmpSingletonMathodMap.ToImmutableDictionary();
            singletonFieldMap = tmpSingletonFieldMap.ToImmutableDictionary();

            return reusedType;
        }

        private static bool CheckIsReusableSingleton(TypeDefinition originalType, IReadOnlyDictionary<string, MethodCallData> callGraph, [NotNullWhen(true)] out MethodDefinition? constructor, out FieldDefinition? vanillaSingletonField) {

            vanillaSingletonField = null;
            constructor = null;

            if (originalType.IsValueType) {
                return false;
            }
            var originalConstructors = originalType.Methods.Where(m => m.IsConstructor && !m.IsStatic).ToArray();
            if (originalConstructors.Length != 1) {
                return false;
            }

            if (originalConstructors.Length == 0) {
                return false;
            }

            constructor = originalConstructors[0];

            if (callGraph.TryGetValue(constructor.GetIdentifier(), out var methodCallData)) {
                if (methodCallData.UsedByMethods.Length != 1) {
                    return false;
                }
                else {
                    var usedByMethod = methodCallData.UsedByMethods[0];
                    foreach (var inst in usedByMethod.Body.Instructions) {
                        if (inst.OpCode != OpCodes.Newobj || inst.Operand is not MethodReference ctorRef || ctorRef.DeclaringType.FullName != originalType.FullName) {
                            continue;
                        }
                        var stackValueUsages = MonoModCommon.Stack.TraceStackValueConsumers(usedByMethod, inst);
                        if (stackValueUsages.Length > 1) {
                            return false;
                        }
                        if (stackValueUsages[0].OpCode != OpCodes.Stsfld) {
                            return false;
                        }
                        var currentSingletonField = ((FieldReference)stackValueUsages[0].Operand).TryResolve();
                        if (currentSingletonField is null) {
                            continue;
                        }
                        // check if the singleton field is the same
                        if (vanillaSingletonField is not null && vanillaSingletonField != currentSingletonField) {
                            vanillaSingletonField = null;
                            return false;
                        }
                        vanillaSingletonField = currentSingletonField;
                    }
                }
            }

            if (vanillaSingletonField is not null && vanillaSingletonField.FieldType.FullName != originalType.FullName) {
                vanillaSingletonField = null;
            }

            return vanillaSingletonField is not null;
        }

        static void ReuseSingletonConstructor(TypeDefinition type, MethodDefinition existingConstructor, FieldDefinition rootContextField, ContextTypeData? declaringType, out Instruction storeSelfPlaceHolder) {

            var rootContextParam = new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, rootContextField.FieldType);
            PatchingCommon.InsertParamAt0AndRemapIndices(existingConstructor.Body, PatchingCommon.InsertParamMode.Insert, rootContextParam);

            // instance field init (not reference any Parameter or field)
            // ldarg.0, call base constructor
            // constructor body

            var callBaseCtor = MonoModCommon.IL.GetBaseConstructorCall(existingConstructor.Body) ?? existingConstructor.Body.Instructions[0];

            var insertTarget = callBaseCtor.Next;

            var ilProcessor = existingConstructor.Body.GetILProcessor();

            ilProcessor.InsertBefore(insertTarget, MonoModCommon.IL.BuildParameterLoad(existingConstructor, existingConstructor.Body, rootContextParam));
            if (declaringType is not null) {
                foreach (var field in declaringType.nestedChain) {
                    ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldfld, field));
                }
            }
            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldarg_0));
            storeSelfPlaceHolder = Instruction.Create(OpCodes.Nop);
            ilProcessor.InsertBefore(insertTarget, storeSelfPlaceHolder);

            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldarg_0));
            ilProcessor.InsertBefore(insertTarget, MonoModCommon.IL.BuildParameterLoad(existingConstructor, existingConstructor.Body, rootContextParam));
            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Stfld, rootContextField));
        }
        static TypeDefinition CreateInstanceConvdType(
            TypeDefinition originalType,
            TypeDefinition contextDef,
            ContextTypeData? declaringType,
            out FieldDefinition rootContextField,
            out MethodDefinition constructor,
            out Instruction storeSelfPlaceHolder) {

            var myContextTypeDef = new TypeDefinition(
                originalType.Namespace,
                originalType.Name + Constants.ContextSuffix,
                originalType.IsNested ? Constants.Modifiers.ContextNestedType : Constants.Modifiers.ContextType,
                originalType.Module.TypeSystem.Object);

            if (declaringType is not null) {
                declaringType.ContextTypeDef.NestedTypes.Add(myContextTypeDef);
            }
            else {
                originalType.Module.Types.Add(myContextTypeDef);
            }

            rootContextField = new FieldDefinition(Constants.RootContextFieldName, Constants.Modifiers.RootContextField, contextDef);
            myContextTypeDef.Fields.Add(rootContextField);
            myContextTypeDef.Methods.Add(constructor = CreateConstructor(myContextTypeDef, rootContextField, declaringType, out storeSelfPlaceHolder));

            return myContextTypeDef;
        }
        static MethodDefinition CreateConstructor(TypeDefinition myContextTypeDef, FieldDefinition rootContextField, ContextTypeData? declaringType, out Instruction storeSelfPlaceHolder) {
            var module = myContextTypeDef.Module;
            var ctor = new MethodDefinition(".ctor", Constants.Modifiers.ContextConstructor, module.TypeSystem.Void) {
                DeclaringType = myContextTypeDef
            };

            var rootContextParam = new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, rootContextField.FieldType);
            ctor.Parameters.Add(rootContextParam);

            ctor.Body = new MethodBody(ctor);
            var insts = ctor.Body.Instructions;

            insts.Add(Instruction.Create(OpCodes.Ldarg_0));
            insts.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));

            insts.Add(MonoModCommon.IL.BuildParameterLoad(ctor, ctor.Body, rootContextParam));
            if (declaringType is not null) {
                foreach (var field in declaringType.nestedChain) {
                    insts.Add(Instruction.Create(OpCodes.Ldfld, field));
                }
            }
            insts.Add(Instruction.Create(OpCodes.Ldarg_0));
            storeSelfPlaceHolder = Instruction.Create(OpCodes.Nop);
            insts.Add(storeSelfPlaceHolder);

            insts.Add(Instruction.Create(OpCodes.Ldarg_0));
            insts.Add(MonoModCommon.IL.BuildParameterLoad(ctor, ctor.Body, rootContextParam));
            insts.Add(Instruction.Create(OpCodes.Stfld, rootContextField));
            insts.Add(Instruction.Create(OpCodes.Ret));

            return ctor;
        }
        static FieldDefinition CreateContextField(TypeDefinition rootContextType, ContextTypeData? declaringInOtherContext, TypeDefinition originalType, TypeDefinition instanceConvdType, bool isReusedSingleton) {

            var fieldName = originalType.Name;
            var declaringType = declaringInOtherContext?.ContextTypeDef ?? rootContextType;

            if (declaringInOtherContext is not null) {
                if (declaringType.NestedTypes.Any(nt => nt.Name == fieldName)) {
                    fieldName = string.Concat(fieldName[0].ToString().ToLower(), fieldName.AsSpan(1));
                }
            }
            else {
                int collisionCount = 0;
                foreach (var existingField in declaringType.Fields) {
                    if (existingField.Name == fieldName || existingField.Name.OrdinalStartsWith(fieldName + "_")) {
                        collisionCount++;
                    }
                }
                if (collisionCount > 0) {
                    fieldName = fieldName + "_" + collisionCount;
                }
            }
            var field = new FieldDefinition(fieldName, FieldAttributes.Public, instanceConvdType);
            declaringType.Fields.Add(field);


            // We need to set the field in the constructor of the declaring type.
            // Unless this is a reused singleton, which assignment logic can be handled by the vanilla code itself.
            if (isReusedSingleton) {
                return field;
            }
            var declaringTypeConstructor = declaringType.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            var instanceConvdTypeConstructor = instanceConvdType.Methods.Single(m => m.IsConstructor && !m.IsStatic);

            Instruction insertTarget;

            if (declaringInOtherContext is null) {
                insertTarget = declaringTypeConstructor.Body.Instructions.Single(inst => inst.OpCode == OpCodes.Ret);
            }
            else {
                insertTarget = declaringTypeConstructor.Body.Instructions.Single(
                    inst => inst is { OpCode.Code: Code.Stfld, Operand: FieldReference { FieldType.FullName: Constants.RootContextFullName } }
                ).Next;
            }

            var ilProcessor = declaringTypeConstructor.Body.GetILProcessor();

            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldarg_0));
            if (declaringInOtherContext is null) {
                // 'this' is root context
                ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Dup));
            }
            else {
                // 'this' is other context, but Parameter 0 (not including 'this') is root context
                ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldarg_1));
            }
            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Newobj, instanceConvdTypeConstructor));
            ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Stfld, field));

            return field;
        }
        public override string ToString() {
            return $"{ContextTypeDef.Name}";
        }
    }
}
