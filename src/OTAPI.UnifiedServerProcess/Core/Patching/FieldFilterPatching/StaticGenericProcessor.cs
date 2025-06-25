using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// Handle static generic classes based on specific circumstances.
    /// <para>If they can be set as global, try not to modify them.</para>
    /// <para>If they are used as type key-value pair storers and it is necessary to allocate an instantiated storer for each server context, then modify them to dictionaries.</para>
    /// </summary>
    public class StaticGenericProcessor() : IFieldFilterArgProcessor, IJumpSitesCacheFeature
    {

        public string Name => "StaticGenericPatcher";

        readonly HashSet<string> ignoredStaticGenericFieldDeclaringFullNames = new HashSet<string>() {
            "ReLogic.Utilities.AttributeUtilities/TypeAttributeCache`2",
            "ReLogic.Content.Asset`1",
        };

        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            ProcessTerraria_Net_NetManager(raw);
            ProcessTerraria_GameContent_Creative_CreativePowerManager(raw);

            foreach (var fieldKV in raw.ModifiedStaticFields.ToArray()) {
                if (fieldKV.Value.DeclaringType.HasGenericParameters) {
                    if (ignoredStaticGenericFieldDeclaringFullNames.Contains(fieldKV.Value.DeclaringType.FullName)) {
                        raw.ModifiedStaticFields.Remove(fieldKV.Key);
                        continue;
                    }
                    throw new Exception("This generic static type not supported yet");
                }
            }
        }
        void ProcessTerraria_Net_NetManager(FilterArgumentSource argument) {

            var netManager = argument.MainModule.GetType("Terraria.Net.NetManager");
            var packetTypeStorage = argument.MainModule.GetType("Terraria.Net.NetManager/PacketTypeStorage`1");

            var netManager_moduleCount = netManager.GetField(nameof(Terraria.Net.NetManager._moduleCount))
                ?? throw new Exception("Terraria.Net.NetManager._moduleCount not found");
            var netManager_modules = netManager.GetField(nameof(Terraria.Net.NetManager._modules))
                ?? throw new Exception("Terraria.Net.NetManager._modules not found");

            var packetTypeStorage_Id = packetTypeStorage.GetField(nameof(Terraria.Net.NetManager.PacketTypeStorage<Terraria.Net.NetModule>.Id))
                ?? throw new Exception("Terraria.Net.NetManager.PacketTypeStorage<Terraria.Net.NetModule>.Id not found");
            var packetTypeStorage_Module = packetTypeStorage.GetField(nameof(Terraria.Net.NetManager.PacketTypeStorage<Terraria.Net.NetModule>.Module))
                ?? throw new Exception("Terraria.Net.NetManager.PacketTypeStorage<Terraria.Net.NetModule>.Module not found");

            RefactorFieldOperate_DictionaryStorage(netManager, packetTypeStorage);

            argument.ModifiedStaticFields.Remove(packetTypeStorage_Id.GetIdentifier());
            argument.ModifiedStaticFields.Remove(packetTypeStorage_Module.GetIdentifier());

            var instanceField = netManager.GetField("Instance");
            argument.ModifiedStaticFields.TryAdd(instanceField.GetIdentifier(), instanceField);
            var packetTypeStorageInstanceField = netManager.GetField("PacketTypeStorageInstance");
            argument.ModifiedStaticFields.TryAdd(packetTypeStorageInstanceField.GetIdentifier(), packetTypeStorageInstanceField);
        }

        void ProcessTerraria_GameContent_Creative_CreativePowerManager(FilterArgumentSource argument) {

            var creativePowerManager = argument.MainModule.GetType("Terraria.GameContent.Creative.CreativePowerManager");
            var powerTypeStorage = argument.MainModule.GetType("Terraria.GameContent.Creative.CreativePowerManager/PowerTypeStorage`1");

            var creativePowerManager_powersCount = creativePowerManager.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager._powersCount))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager._powersCount not found");
            var creativePowerManager_powersById = creativePowerManager.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager._powersById))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager._powersById not found");
            var creativePowerManager_powersByName = creativePowerManager.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager._powersByName))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager._powersByName not found");

            var powerTypeStorage_Id = powerTypeStorage.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Id))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Id not found");
            var powerTypeStorage_Name = powerTypeStorage.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Name))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Name not found");
            var powerTypeStorage_Power = powerTypeStorage.GetField(nameof(Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Power))
                ?? throw new Exception("Terraria.GameContent.Creative.CreativePowerManager.PowerTypeStorage<Terraria.GameContent.Creative.ICreativePower>.Power not found");

            RefactorFieldOperate_DictionaryStorage(creativePowerManager, powerTypeStorage);

            argument.ModifiedStaticFields.Remove(powerTypeStorage_Id.GetIdentifier());
            argument.ModifiedStaticFields.Remove(powerTypeStorage_Name.GetIdentifier());
            argument.ModifiedStaticFields.Remove(powerTypeStorage_Power.GetIdentifier());
            var powersCountField = creativePowerManager.GetField("PowerTypeStorageInstance");
            argument.ModifiedStaticFields.TryAdd(powersCountField.GetIdentifier(), powersCountField);
        }

        public readonly struct TypeInitializationParams
        {
            public readonly string Prefix;

            public readonly TypeDefinition containingType;
            public readonly TypeDefinition staticGenericType;
            public readonly GenericParameter origGenericParam;

            public readonly ModuleDefinition module;

            public readonly TypeReference Void;
            public readonly TypeReference Bool;
            public readonly TypeReference Object;

            public readonly TypeReference sysType;
            public readonly TypeReference rtHandleType;
            public readonly TypeReference activatorType;

            public readonly TypeReference dictTypePatten;

            public TypeInitializationParams(TypeDefinition containingType, TypeDefinition staticGenericType) {
                if (staticGenericType.GenericParameters.Count == 0) {
                    throw new ArgumentException("It is not a generic type");
                }
                if (staticGenericType.GenericParameters.Count > 1) {
                    throw new ArgumentException("Only support one generic TrackingParameter");
                }
                origGenericParam = staticGenericType.GenericParameters[0];

                Prefix = staticGenericType.Name.Split('`')[0];

                this.containingType = containingType;
                this.staticGenericType = staticGenericType;
                module = containingType.Module;

                Void = module.TypeSystem.Void;
                Object = module.TypeSystem.Object;
                Bool = module.TypeSystem.Boolean;
                sysType = new TypeReference(nameof(System), nameof(Type), module, module.TypeSystem.CoreLibrary);
                rtHandleType = new TypeReference(nameof(System), nameof(RuntimeTypeHandle), module, module.TypeSystem.CoreLibrary);
                activatorType = new TypeReference(nameof(System), nameof(Activator), module, module.TypeSystem.CoreLibrary);

                var collectionScope = module.AssemblyReferences.First(a => a.Name == "System.Collections");

                dictTypePatten = new TypeReference("System.Collections.Generic", "Dictionary`2", module, collectionScope);
                var genericParamKey = new GenericParameter(dictTypePatten);
                var genericParamValue = new GenericParameter(dictTypePatten);
                dictTypePatten.GenericParameters.Add(genericParamKey);
                dictTypePatten.GenericParameters.Add(genericParamValue);
            }
        }
        public readonly ref struct MethodReferenceParams
        {
            public readonly MethodReference TypeOfT;
            public readonly MethodReference CreateInstancePatten;
            readonly TypeInitializationParams typeParams;
            public MethodReference CreateDictTryGet(TypeReference genericInstancedDictType) {

                var dictTypePatten = typeParams.dictTypePatten;
                var genericParamKey = dictTypePatten.GenericParameters[0];
                var genericParamValue = dictTypePatten.GenericParameters[1];

                var dictTryGet = new MethodReference(nameof(Dictionary<Type, object>.TryGetValue), typeParams.Bool, dictTypePatten) {
                    HasThis = true,
                };
                dictTryGet.Parameters.Add(new ParameterDefinition(genericParamKey));
                dictTryGet.Parameters.Add(new ParameterDefinition("", ParameterAttributes.Out, genericParamValue.MakeByReferenceType()));
                dictTryGet.DeclaringType = genericInstancedDictType;

                return dictTryGet;
            }
            public MethodReference CreateSetItem(TypeReference genericInstancedDictType) {

                var dictTypePatten = typeParams.dictTypePatten;
                var genericParamKey = dictTypePatten.GenericParameters[0];
                var genericParamValue = dictTypePatten.GenericParameters[1];

                var dictSetItem = new MethodReference("set_Item", typeParams.Void, dictTypePatten) {
                    HasThis = true,
                };
                dictSetItem.Parameters.Add(new ParameterDefinition(genericParamKey));
                dictSetItem.Parameters.Add(new ParameterDefinition(genericParamValue));
                dictSetItem.DeclaringType = genericInstancedDictType;

                return dictSetItem;
            }
            public readonly MethodReference CreateActivatorCreateInstance(TypeReference T) {
                GenericInstanceMethod genericInstanceMethod = new GenericInstanceMethod(CreateInstancePatten);
                genericInstanceMethod.GenericArguments.Add(T);
                return genericInstanceMethod;
            }

            public MethodReferenceParams(in TypeInitializationParams typeParams) {
                this.typeParams = typeParams;

                var module = typeParams.module;
                var sysType = typeParams.sysType;
                var rtHandleType = typeParams.rtHandleType;
                var activatorType = typeParams.activatorType;

                TypeOfT = module.ImportReference(sysType.Resolve().Methods.Single(m => m.Name == nameof(Type.GetTypeFromHandle)));

                CreateInstancePatten = new MethodReference(nameof(Activator.CreateInstance), module.TypeSystem.Object, activatorType) {
                    HasThis = false,
                };
                var createInstanceGenericParam = new GenericParameter(CreateInstancePatten);
                CreateInstancePatten.GenericParameters.Add(createInstanceGenericParam);
                CreateInstancePatten.ReturnType = createInstanceGenericParam;
            }
        }
        public readonly struct ItemParams(TypeDefinition itemType, MethodDefinition itemCtor, Dictionary<FieldDefinition, FieldDefinition> fieldMap)
        {
            public readonly Dictionary<FieldDefinition, FieldDefinition> FieldMap = fieldMap;
            public readonly TypeDefinition ItemType = itemType;
            public readonly MethodDefinition ItemCtor = itemCtor;

            public readonly MethodReference CreateGenericInstancedCtor(TypeReference T, out GenericInstanceType genericInstancedItemType) {
                genericInstancedItemType = new GenericInstanceType(ItemType);
                genericInstancedItemType.GenericArguments.Add(T);
                var method = new MethodReference(".ctor", ItemCtor.ReturnType, genericInstancedItemType) {
                    HasThis = true,
                };
                foreach (var parameter in ItemCtor.Parameters) {
                    method.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                }
                return method;
            }
        }
        public readonly struct ContainerParams(FieldDefinition containerField, TypeDefinition containerType, FieldDefinition innerContainerField, TypeReference innerContainerType)
        {
            public readonly FieldDefinition containerField = containerField;
            public readonly TypeDefinition containerType = containerType;
            public readonly FieldDefinition innerContainerField = innerContainerField;
            public readonly TypeReference innerContainerType = innerContainerType;
        }
        private static void CreateItemTypeAndFieldMap(in TypeInitializationParams typeParams, in MethodReferenceParams methodParams, out ItemParams itemParams) {
            var staticGenericType = typeParams.staticGenericType;
            var origGenericParam = typeParams.origGenericParam;

            var name = staticGenericType.Name.Split('`')[0];
            var itemType = new TypeDefinition("", name + "Item", TypeAttributes.NestedPublic | TypeAttributes.Class, staticGenericType.Module.TypeSystem.Object);
            var itemGenericParam = new GenericParameter(origGenericParam.Name, itemType);
            itemType.GenericParameters.Add(itemGenericParam);

            var fields = staticGenericType.Fields.Where(f => f.IsStatic).ToArray();
            var origGenericField = fields.FirstOrDefault(f => f.FieldType == origGenericParam)
                ?? throw new Exception("The type does not contain a fieldKV of the generic type");

            var fieldMap = new Dictionary<FieldDefinition, FieldDefinition>();

            foreach (var origField in fields) {
                var fieldType = origField.FieldType;
                if (origField.FieldType == origGenericParam) {
                    fieldType = itemGenericParam;
                }
                var field = new FieldDefinition(origField.Name, origField.Attributes &= ~FieldAttributes.Static, fieldType);
                fieldMap.Add(origField, field);
                itemType.Fields.Add(field);
            }

            var genericField = fieldMap[origGenericField];
            var genericInstanceItemType = new GenericInstanceType(itemType);
            genericInstanceItemType.GenericArguments.Add(itemGenericParam);
            var genericInstanceField = new FieldReference(genericField.Name, genericField.FieldType, genericInstanceItemType);

            var itemCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                typeParams.Void);
            itemCtor.Parameters.Add(new ParameterDefinition("defaultValue", ParameterAttributes.None, itemGenericParam));
            itemCtor.Body = new MethodBody(itemCtor);
            var itemCtorBody = itemCtor.Body.Instructions;
            itemCtorBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            itemCtorBody.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", typeParams.Void, typeParams.Object) { HasThis = true }));
            itemCtorBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            itemCtorBody.Add(Instruction.Create(OpCodes.Ldarg_1));
            itemCtorBody.Add(Instruction.Create(OpCodes.Stfld, genericInstanceField));
            itemCtorBody.Add(Instruction.Create(OpCodes.Ret));
            itemType.Methods.Add(itemCtor);

            typeParams.containingType.NestedTypes.Add(itemType);

            itemParams = new ItemParams(itemType, itemCtor, fieldMap);
        }
        private static void CreateContainerType(in TypeInitializationParams typeParams, out ContainerParams containerParams) {
            var containingType = typeParams.containingType;
            var containerType = new TypeDefinition("", typeParams.Prefix + "Container", TypeAttributes.NestedPublic | TypeAttributes.Class, containingType.Module.TypeSystem.Object);
            containingType.NestedTypes.Add(containerType);
            var containerCtor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, typeParams.Void);
            containerCtor.Body = new MethodBody(containerCtor);

            var innerContainerType = new GenericInstanceType(typeParams.dictTypePatten);
            innerContainerType.GenericArguments.Add(typeParams.sysType);
            innerContainerType.GenericArguments.Add(typeParams.Object);
            var innerContainerField = new FieldDefinition("data", FieldAttributes.Public, innerContainerType);
            containerType.Fields.Add(innerContainerField);

            var containerCtorBody = containerCtor.Body.Instructions;

            containerCtorBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            containerCtorBody.Add(Instruction.Create(OpCodes.Newobj, new MethodReference(".ctor", typeParams.Void, innerContainerType) { HasThis = true }));
            containerCtorBody.Add(Instruction.Create(OpCodes.Stfld, innerContainerField));
            containerCtorBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            containerCtorBody.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", typeParams.Void, typeParams.Object) { HasThis = true }));
            containerCtorBody.Add(Instruction.Create(OpCodes.Ret));
            containerType.Methods.Add(containerCtor);

            var containerField = new FieldDefinition(typeParams.Prefix + "Instance", FieldAttributes.Public | FieldAttributes.Static, containerType);
            containingType.Fields.Add(containerField);

            var containingTypeCtorDef = containingType.Methods.Single(m => m.Name == ".ctor" && !m.IsStatic);
            var callBaseCtor = containingTypeCtorDef.Body.Instructions.Single(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == ".ctor");
            var loadThisBeforeCallBaseCtor = callBaseCtor.Previous;
            containingTypeCtorDef.Body.GetILProcessor()
                .InsertBeforeSeamlessly(ref loadThisBeforeCallBaseCtor, [
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Newobj, containerCtor),
                    Instruction.Create(OpCodes.Stfld, containerField)
                ]);

            containerParams = new ContainerParams(containerField, containerType, innerContainerField, innerContainerType);
        }
        static void CreateTypedDataGetMethod(TypeDefinition containingType, TypeDefinition staticGenericType, out FieldDefinition containerField, out MethodDefinition typedDataGetMethod, out Dictionary<FieldDefinition, FieldDefinition> fieldMap) {
            // init type and method params
            var typeParams = new TypeInitializationParams(containingType, staticGenericType);
            var methodParams = new MethodReferenceParams(typeParams);

            // create item type and fields
            CreateItemTypeAndFieldMap(typeParams, methodParams, out var itemParams);
            fieldMap = itemParams.FieldMap;

            // create container type and fields
            CreateContainerType(typeParams, out var containerParams);

            typedDataGetMethod = new MethodDefinition("Get", MethodAttributes.Public | MethodAttributes.HideBySig, itemParams.ItemType);
            var getMethodGenericParam = new GenericParameter(typeParams.origGenericParam.Name, typedDataGetMethod);
            typedDataGetMethod.GenericParameters.Add(getMethodGenericParam);

            var itemCtorRef = itemParams.CreateGenericInstancedCtor(getMethodGenericParam, out var returnType);
            typedDataGetMethod.ReturnType = returnType;

            typedDataGetMethod.Body = new MethodBody(typedDataGetMethod);
            var tmpObjValue = new VariableDefinition(typeParams.Object);
            typedDataGetMethod.Body.Variables.Add(tmpObjValue);

            var loadObj = Instruction.Create(OpCodes.Ldloc_0);

            var getValueBody = typedDataGetMethod.Body.Instructions;
            getValueBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            getValueBody.Add(Instruction.Create(OpCodes.Ldfld, containerParams.innerContainerField));
            getValueBody.Add(Instruction.Create(OpCodes.Ldtoken, getMethodGenericParam));
            getValueBody.Add(Instruction.Create(OpCodes.Call, methodParams.TypeOfT));
            getValueBody.Add(Instruction.Create(OpCodes.Ldloca_S, tmpObjValue));
            getValueBody.Add(Instruction.Create(OpCodes.Callvirt, methodParams.CreateDictTryGet(containerParams.innerContainerType)));
            getValueBody.Add(Instruction.Create(OpCodes.Brtrue_S, loadObj));

            getValueBody.Add(Instruction.Create(OpCodes.Ldarg_0));
            getValueBody.Add(Instruction.Create(OpCodes.Ldfld, containerParams.innerContainerField));
            getValueBody.Add(Instruction.Create(OpCodes.Ldtoken, getMethodGenericParam));
            getValueBody.Add(Instruction.Create(OpCodes.Call, methodParams.TypeOfT));

            getValueBody.Add(Instruction.Create(OpCodes.Call, methodParams.CreateActivatorCreateInstance(getMethodGenericParam)));

            getValueBody.Add(Instruction.Create(OpCodes.Newobj, itemCtorRef));
            getValueBody.Add(Instruction.Create(OpCodes.Dup));

            getValueBody.Add(Instruction.Create(OpCodes.Stloc_0));
            getValueBody.Add(Instruction.Create(OpCodes.Callvirt, methodParams.CreateSetItem(containerParams.innerContainerType)));
            getValueBody.Add(loadObj);
            Instruction.Create(OpCodes.Castclass, returnType);
            getValueBody.Add(Instruction.Create(OpCodes.Ret));


            containerParams.containerType.Methods.Add(typedDataGetMethod);

            containerField = containerParams.containerField;
        }
        void RefactorFieldOperate_DictionaryStorage(TypeDefinition containingType, TypeDefinition staticGenericType) {

            CreateTypedDataGetMethod(containingType, staticGenericType, out var containerField, out var typedDataGetMethod, out var fieldMap);
            foreach (var method in containingType.Methods) {
                var jumpSites = this.GetMethodJumpSites(method);
                var ilProcessor = method.Body.GetILProcessor();

                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    switch (instruction.OpCode.Code) {
                        case Code.Ldsflda:
                        case Code.Ldsfld: {
                                var fieldRef = (FieldReference)instruction.Operand;
                                var field = fieldRef.TryResolve();
                                if (field is null) {
                                    continue;
                                }
                                if (!fieldMap.TryGetValue(field, out var newField)) {
                                    continue;
                                }
                                var genericInstance = (GenericInstanceType)fieldRef.DeclaringType;
                                var genericInstancedGetMethod = new GenericInstanceMethod(typedDataGetMethod);
                                genericInstancedGetMethod.GenericArguments.Add(genericInstance.GenericArguments[0]);

                                var genericInstancedDeclaringType = new GenericInstanceType(newField.DeclaringType);
                                genericInstancedDeclaringType.GenericArguments.Add(genericInstance.GenericArguments[0]);
                                var genericInstancedFieldRef = new FieldReference(field.Name, field.FieldType, genericInstancedDeclaringType);

                                Instruction[] insertBefores = [
                                    Instruction.Create(OpCodes.Ldarg_0),
                                    Instruction.Create(OpCodes.Ldfld, containerField),
                                    Instruction.Create(OpCodes.Callvirt, genericInstancedGetMethod),
                                    ];
                                var insertTarget = instruction;
                                ilProcessor.InsertBeforeSeamlessly(ref insertTarget, insertBefores);
                                insertTarget.Operand = genericInstancedFieldRef;
                                insertTarget.OpCode = insertTarget.OpCode.Code == Code.Ldsflda ? OpCodes.Ldflda : OpCodes.Ldfld;
                                break;
                            }
                        case Code.Stsfld: {
                                var fieldRef = (FieldReference)instruction.Operand;
                                var field = fieldRef.TryResolve();
                                if (field is null) {
                                    continue;
                                }
                                if (!fieldMap.TryGetValue(field, out var newField)) {
                                    continue;
                                }
                                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSites)) {
                                    var loadValue = path.ParametersSources[0].Instructions.First();

                                    var genericInstance = (GenericInstanceType)fieldRef.DeclaringType;
                                    var genericInstancedGetMethod = new GenericInstanceMethod(typedDataGetMethod);
                                    genericInstancedGetMethod.GenericArguments.Add(genericInstance.GenericArguments[0]);

                                    var genericInstancedDeclaringType = new GenericInstanceType(newField.DeclaringType);
                                    genericInstancedDeclaringType.GenericArguments.Add(genericInstance.GenericArguments[0]);
                                    var genericInstancedFieldRef = new FieldReference(field.Name, field.FieldType, genericInstancedDeclaringType);

                                    Instruction[] insertBefores = [
                                        Instruction.Create(OpCodes.Ldarg_0),
                                        Instruction.Create(OpCodes.Ldfld, containerField),
                                        Instruction.Create(OpCodes.Callvirt, genericInstancedGetMethod),
                                        ];
                                    ilProcessor.InsertBeforeSeamlessly(ref loadValue, insertBefores);
                                    instruction.Operand = genericInstancedFieldRef;
                                    instruction.OpCode = OpCodes.Stfld;
                                }
                                break;
                            }

                        default:
                            break;
                    }
                }
            }
        }
    }
}
