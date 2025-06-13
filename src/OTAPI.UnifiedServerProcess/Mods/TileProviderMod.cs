#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
using Microsoft.Xna.Framework;
using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Commons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Terraria;
using Terraria.DataStructures;

[Modification(ModType.PostMerge, "Replace the old tile with struct TileData", ModPriority.Last)]
[MonoMod.MonoModIgnore]
void PatchTileSystem(ModFwModder modder) {
    new TileSystemPatchLogic(modder).Patch();
}

/*  RefTileModel (Named as refTile in variable naming) and TileData& (Named as tileRef in variable naming) are completely 
 *  different entities. Even when RefTileModel internally holds a reference to TileData through some implementation, it 
 *  also has the property of being able to be safely stored on the heap. */

[MonoMod.MonoModIgnore]
class TileSystemPatchLogic
{
    readonly ModFwModder modder;

    readonly TypeDefinition tileTypeDef;
    readonly TypeDefinition refTileTypeDef;
    readonly TypeDefinition tileTypeOldDef;
    readonly TypeDefinition tileTypeImplDef;
    bool IsTileType(TypeReference type) {
        if (type is ByReferenceType byReferenceType) {
            return IsTileType(byReferenceType.ElementType);
        }
        return type.FullName == tileTypeDef.FullName || type.FullName == refTileTypeDef.FullName || type.FullName == tileTypeOldDef.FullName;
    }

    readonly MethodDefinition tileCreate;
    readonly MethodDefinition tileCreateWithExistingTile;

    readonly MethodDefinition refTile_GetTempMDef;
    readonly MethodDefinition refTile_GetDataMDef;

    readonly TypeDefinition tileCollectionDef;
    readonly TypeReference tileCollectionDefOld;
    readonly MethodDefinition tileCollection_CreateMDef;
    readonly MethodDefinition tileCollection_getItemMDef;
    readonly MethodDefinition tileCollection_GetRefTileMDef;
    readonly FieldDefinition tileCollectionFieldDefInMain;

    readonly Dictionary<string, string> tileNameMap;


    public TileSystemPatchLogic(ModFwModder modder) {
        this.modder = modder;

        tileTypeDef = modder.Module.GetDefinition<Terraria.TileData>();
        refTileTypeDef = modder.Module.GetDefinition<Terraria.RefTileData>();
        tileTypeOldDef = modder.Module.GetDefinition<Terraria.ITile>();
        tileTypeImplDef = modder.Module.GetDefinition<Terraria.Tile>();

        tileCreate = tileTypeDef.Methods.Single(x => x.Name == nameof(Terraria.TileData.New) && x.Parameters.Count == 0);
        tileCreateWithExistingTile = tileTypeDef.Methods.Single(x => x.Name == nameof(Terraria.TileData.New) && x.Parameters.Count == 1);

        refTile_GetTempMDef = refTileTypeDef.Method("get_" + nameof(RefTileData.Temporary));
        refTile_GetDataMDef = refTileTypeDef.Method("get_" + nameof(RefTileData.Data));

        tileCollectionDef = modder.Module.GetDefinition<Terraria.TileCollection>();
        tileCollection_CreateMDef = tileCollectionDef.Method(nameof(Terraria.TileCollection.Create));
        tileCollection_getItemMDef = tileCollectionDef.Method("get_Item");
        tileCollection_GetRefTileMDef = tileCollectionDef.Method(nameof(TileCollection.GetRefTile));

        tileCollectionFieldDefInMain = modder.Module.GetType(typeof(Terraria.Main).FullName).FindField(nameof(Terraria.Main.tile))!;
        tileCollectionDefOld = tileCollectionFieldDefInMain.FieldType;
        tileCollectionFieldDefInMain.FieldType = tileCollectionDef;

        var oldTileFullName = tileTypeOldDef.FullName;

        tileNameMap = new() {
            { oldTileFullName, tileTypeDef.FullName },
            { tileCollectionDefOld.FullName, tileCollectionDef.FullName }
        };
    }

    public void Patch() {

        Replace_GenericParamAndArgs(modder);

        Analyze_ModifiedTileParameter(modder, out var modifiedTileParameters);

        Replace_TileCollection(modder);

        Adjust_MFWHMethods(modder, modifiedTileParameters);

        Adjust_MethodReturnTileRef(modder);

        Analyze_ComponentsNeedAdjust(modder, modifiedTileParameters, out var fieldShouldAdjust, out var methodShouldAdjust);

        var oldTileFullName = tileTypeOldDef.FullName;

        Dictionary<string, MethodDefinition> allMethods = modder.Module
            .GetAllTypes()
            .Where(t => !t.Name.StartsWith("<>f__AnonymousType"))
            .SelectMany(x => x.Methods)
            .ToDictionary(x => x.GetIdentifier(), x => x);

        Adjust_RelinkModifiedComponets(modder, modifiedTileParameters, allMethods, fieldShouldAdjust, methodShouldAdjust, out var fieldReferences, out var methodsReferences);

        var tileOperateMethodsArray = methodShouldAdjust.Values.ToArray();

        Adjust_RefFeature(modder, modifiedTileParameters, methodShouldAdjust, allMethods, tileOperateMethodsArray);
        Adjust_UseRefTileModel(modder, modifiedTileParameters, fieldReferences: fieldReferences, methodsReferences: methodsReferences, tileOperateMethodsArray);

        Adjust_CleanupOldTile();
    }

    private void Adjust_CleanupOldTile() {
        modder.Module.Types.Remove(tileTypeOldDef);
        tileTypeImplDef.Interfaces.Clear();
        foreach (var method in tileTypeImplDef.Methods.ToArray()) {
            if (!method.IsStatic) {
                tileTypeImplDef.Methods.Remove(method);
            }
        }
        foreach (var field in tileTypeImplDef.Fields.ToArray()) {
            if (!field.IsStatic) {
                tileTypeImplDef.Fields.Remove(field);
            }
        }
        foreach (var prop in tileTypeImplDef.Properties.ToArray()) {
            if (prop.HasThis) {
                tileTypeImplDef.Properties.Remove(prop);
            }
        }
        tileTypeImplDef.Attributes |= Mono.Cecil.TypeAttributes.Sealed;
        tileTypeImplDef.Attributes |= Mono.Cecil.TypeAttributes.Abstract;
    }

    private void Adjust_UseRefTileModel(ModFwModder modder,
        Dictionary<string, HashSet<int>> modifiedTileParameters,
        Dictionary<string, Dictionary<string, MethodDefinition>> fieldReferences,
        Dictionary<string, Dictionary<string, MethodDefinition>> methodsReferences, 
        MethodDefinition[] tileOperateMethodsArray) {

        Dictionary<string, FieldDefinition> refTileFields = new();
        Dictionary<string, MethodDefinition> visited = new();
        Dictionary<string, MethodDefinition> refTileTransferMethods = new();

        Dictionary<string, (MethodDefinition refVersion, bool applied)> retRefModelMethodMaps = new() {
            { modder.Module.GetType("OTAPI.Hooks/Tile").Methods.First(m => m.Name == "InvokeCreate" && m.Parameters.Count == 0).GetIdentifier(), (refTile_GetTempMDef, true) },
            { tileCreate.GetIdentifier(), (refTile_GetTempMDef, true) },
            { tileCollectionDef.Method("get_Item").GetIdentifier(), (tileCollection_GetRefTileMDef, true) }
        };

        foreach (var method in tileOperateMethodsArray) {

            if (!method.HasBody) {
                continue;
            }

            foreach (var inst in method.Body.Instructions) {
                if (!inst.MatchLdflda(out var field) || !IsTileType(field.FieldType)) {
                    continue;
                }
                var usage = MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, inst);
                if (usage.Length != 1 || !usage[0].MatchCallOrCallvirt(out var methodReference) || !modifiedTileParameters.TryGetValue(methodReference.GetIdentifier(), out var indexes)) {
                    continue;
                }
                if (methodReference.Name.StartsWith("mfwh_")) {
                    continue;
                }
                var fieldDef = field.Resolve();
                fieldDef.FieldType = refTileTypeDef;
                refTileFields.TryAdd(fieldDef.GetIdentifier(), fieldDef);
                visited.TryAdd(method.GetIdentifier(), method);
            }
        }
        Stack<MethodDefinition> works = new(visited.Values);
        visited.Clear();
        while (works.Count > 0) {
            var currentMethod = works.Pop();
            if (!visited.TryAdd(currentMethod.GetIdentifier(), currentMethod)) {
                continue;
            }
            if (!currentMethod.HasBody) {
                continue;
            }
            if (currentMethod.DeclaringType.GetRootDeclaringType().Namespace.StartsWith("HookEvents.")) {
                continue;
            }
            var ilProcessor = currentMethod.Body.GetILProcessor();
            var jumpSites = MonoModCommon.Stack.BuildJumpSitesMap(currentMethod);
            HashSet<Instruction> skipArgmentOperations = new();
            Dictionary<int, TypeReference> paramOriginalType = [];
            Dictionary<int, VariableDefinition> localMap = [];

            Dictionary<string, MethodDefinition> usedFields = new();

            var currentMethodOldId = currentMethod.GetIdentifier();
            Instruction[] instArray = currentMethod.Body.Instructions.ToArray();
            for (int i = 0; i < instArray.Length; i++) {
                Instruction? inst = instArray[i];
                FieldDefinition? refTileFieldDef = null;
                if (inst.Operand is FieldReference fieldReference && refTileFields.TryGetValue(fieldReference.GetIdentifier(), out refTileFieldDef)) {
                    fieldReference.FieldType = refTileTypeDef;
                    if (fieldReferences.TryGetValue(fieldReference.GetIdentifier(), out var usedFieldMethods)) {
                        foreach (var kv in usedFieldMethods) {
                            usedFields.TryAdd(kv.Key, kv.Value);
                        }
                    }
                }
                switch (inst.OpCode.Code) {
                    case Code.Ldfld:
                        if (refTileFieldDef is null) {
                            break;
                        }
                        inst.OpCode = OpCodes.Ldflda;
                        ilProcessor.InsertAfter(inst, [
                            Instruction.Create(OpCodes.Call, refTile_GetDataMDef),
                            Instruction.Create(OpCodes.Ldobj),
                        ]);
                        break;
                    case Code.Ldflda:
                        if (refTileFieldDef is null) {
                            break;
                        }
                        ilProcessor.InsertAfter(inst, Instruction.Create(OpCodes.Call, refTile_GetDataMDef));
                        break;
                    case Code.Stfld:
                        if (refTileFieldDef is null) {
                            break;
                        }
                        foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(currentMethod, inst, jumpSites)) {
                            var loadValueBegin = path.ParametersSources[1].Instructions.First();
                            var loadValueEnd = path.ParametersSources[1].Instructions.Last();
                            Adjust_UseRefTileModel(path.ParametersSources[1], ref inst, loadValueBegin, loadValueEnd);
                        }
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        var callee = (MethodReference)inst.Operand!;
                        if (!refTileTransferMethods.TryGetValue(callee.GetIdentifier(), out var transferMethod)) {
                            break;
                        }
                        foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(currentMethod, inst, jumpSites)) {
                            for (int paramIndexInculdeThis = 0; paramIndexInculdeThis < path.ParametersSources.Length; paramIndexInculdeThis++) {
                                int paramIndexExculdeThis = paramIndexInculdeThis;
                                if (callee.HasThis && inst.OpCode.Code != Code.Newobj) {
                                    paramIndexExculdeThis -= 1;
                                }
                                if (transferMethod.Parameters[paramIndexExculdeThis].ParameterType.FullName != refTileTypeDef.FullName) {
                                    continue;
                                }

                                var loadValueBegin = path.ParametersSources[paramIndexInculdeThis].Instructions.First();
                                var loadValueEnd = path.ParametersSources[paramIndexInculdeThis].Instructions.Last();

                                Adjust_UseRefTileModel(path.ParametersSources[paramIndexInculdeThis], ref inst, loadValueBegin, loadValueEnd);
                            }
                        }
                        inst.Operand = MonoModCommon.Structure.DeepMapMethodReference(transferMethod, new());
                        break;
                }
            }
            foreach (var m in usedFields.Values) {
                works.Push(m);
            }
            Adjust_RefTileLocal();
            Adjust_RefTileParamAndIncrementAdd();

            continue;

            bool Adjust_UseRefTileModel<TSource>(TSource source, ref Instruction inst, Instruction loadValueBegin, Instruction loadValueEnd) where TSource : MonoModCommon.Stack.ArgumentSource {
                Instruction rawLoadTile = loadValueEnd;
                if (loadValueEnd.OpCode == OpCodes.Ldobj) {
                    var previous = loadValueEnd.Previous;
                    while (previous.OpCode.StackBehaviourPush == StackBehaviour.Push0) {
                        previous = previous.Previous;
                    }
                    rawLoadTile = previous;
                }
                if (MonoModCommon.IL.TryGetReferencedParameter(currentMethod, rawLoadTile, out var parameter) 
                    && IsTileType(parameter.ParameterType) 
                    && parameter.ParameterType.FullName != refTileTypeDef.FullName) {

                    paramOriginalType[parameter.Index] = parameter.ParameterType;
                    parameter.ParameterType = refTileTypeDef;
                    var newInst = MonoModCommon.IL.BuildParameterLoad(currentMethod, currentMethod.Body, parameter);
                    rawLoadTile.OpCode = newInst.OpCode;
                    rawLoadTile.Operand = newInst.Operand;

                    var skipCount = Array.IndexOf(source.Instructions, rawLoadTile) + 1;
                    if (skipCount == 0) {
                        throw new Exception();
                    }

                    foreach (var rest in source.Instructions.Skip(skipCount)) {
                        rest.OpCode = OpCodes.Nop;
                        rest.Operand = null;
                    }
                    skipArgmentOperations.Add(rawLoadTile);
                    return true;
                }
                else if (rawLoadTile.OpCode == OpCodes.Call || rawLoadTile.OpCode == OpCodes.Callvirt) {
                    var id = ((MethodReference)rawLoadTile.Operand).GetIdentifier();
                    if (retRefModelMethodMaps.TryGetValue(id, out var retRefModelMethod)) {
                        rawLoadTile.Operand = retRefModelMethod.refVersion;
                        if (!retRefModelMethod.applied) {
                            retRefModelMethod.refVersion.DeclaringType.Methods.Add(retRefModelMethod.refVersion);
                            retRefModelMethod.applied = true;
                            retRefModelMethodMaps[id] = retRefModelMethod;
                        }
                        return true;
                    }
                    switch (inst.OpCode.Code) {
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                        case Code.Stloc:
                            var local = MonoModCommon.IL.GetReferencedVariable(currentMethod, inst);
                            var mappedLocal = localMap[local.Index];
                            ilProcessor.InsertAfter(rawLoadTile, [
                                MonoModCommon.IL.BuildVariableStore(currentMethod, currentMethod.Body, mappedLocal),
                                MonoModCommon.IL.BuildVariableLoadAddress(currentMethod, currentMethod.Body, mappedLocal),
                                Instruction.Create(OpCodes.Call, refTile_GetDataMDef),
                            ]);
                            break;
                    }
                }
                else if (MonoModCommon.IL.TryGetReferencedVariable(currentMethod, rawLoadTile, out var variable)
                    && IsTileType(variable.VariableType)
                    && variable.VariableType.FullName != refTileTypeDef.FullName) {

                    if (!localMap.TryGetValue(variable.Index, out var mappedLocal)) {
                        localMap[variable.Index] = mappedLocal = new VariableDefinition(refTileTypeDef);
                        currentMethod.Body.Variables.Add(mappedLocal);
                    }
                    var newInst = MonoModCommon.IL.BuildVariableLoad(currentMethod, currentMethod.Body, mappedLocal);
                    rawLoadTile.OpCode = newInst.OpCode;
                    rawLoadTile.Operand = newInst.Operand;

                    var skipCount = Array.IndexOf(source.Instructions, rawLoadTile) + 1;
                    if (skipCount == 0) {
                        throw new Exception();
                    }

                    foreach (var rest in source.Instructions.Skip(skipCount)) {
                        rest.OpCode = OpCodes.Nop;
                        rest.Operand = null;
                    }
                    return true;
                }
                return false;
            }

            void Adjust_RefTileLocal() {
                if (localMap.Count == 0) {
                    return;
                }
                bool anyLocalModified = false;
                do {
                    anyLocalModified = false;
                    Instruction[] array = [.. currentMethod.Body.Instructions];

                    for (int i = 0; i < array.Length; i++) {
                        var inst = array[i];
                        if (!MonoModCommon.IL.TryGetReferencedVariable(currentMethod, inst, out var local) || !localMap.TryGetValue(local.Index, out var mappedLocal)) {
                            continue;
                        }
                        switch (inst.OpCode.Code) {
                            case Code.Stloc_0:
                            case Code.Stloc_1:
                            case Code.Stloc_2:
                            case Code.Stloc_3:
                            case Code.Stloc_S:
                            case Code.Stloc:
                                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(currentMethod, inst, jumpSites)) {
                                    var loadValueBegin = path.ParametersSources[0].Instructions.First();
                                    var loadValueEnd = path.ParametersSources[0].Instructions.Last();
                                    if (Adjust_UseRefTileModel(path.ParametersSources[0], ref inst, loadValueBegin, loadValueEnd)) {
                                        anyLocalModified = true;
                                    }
                                }
                                break;
                        }
                    }
                }
                while (anyLocalModified);
            }

            void Adjust_RefTileParamAndIncrementAdd() {
                if (!currentMethod.Parameters.Any(p => p.ParameterType.FullName == refTileTypeDef.FullName)) {
                    return;
                }

                visited.TryAdd(currentMethod.GetIdentifier(), currentMethod);
                refTileTransferMethods[currentMethodOldId] = currentMethod;

                Instruction[] array = [.. currentMethod.Body.Instructions];

                for (int i = 0; i < array.Length; i++) {
                    var inst = array[i];
                    if (!MonoModCommon.IL.TryGetReferencedParameter(currentMethod, inst, out var parameter) || parameter.ParameterType.FullName != refTileTypeDef.FullName) {
                        continue;
                    }
                    var originalType = paramOriginalType[parameter.Index];
                    if (skipArgmentOperations.Contains(inst)) {
                        continue;
                    }
                    switch (inst.OpCode.Code) {
                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                        case Code.Ldarg_S:
                        case Code.Ldarg:
                            var tmp = MonoModCommon.IL.BuildParameterLoadAddress(currentMethod, currentMethod.Body, parameter);
                            inst.OpCode = tmp.OpCode;
                            if (originalType is ByReferenceType) {
                                ilProcessor.InsertBeforeSeamlessly(ref inst, Instruction.Create(OpCodes.Call, refTile_GetDataMDef));
                            }
                            else {
                                ilProcessor.InsertBeforeSeamlessly(ref inst, [
                                    Instruction.Create(OpCodes.Call, refTile_GetDataMDef),
                                Instruction.Create(OpCodes.Ldobj),
                            ]);
                            }
                            break;
                        case Code.Ldarga_S:
                        case Code.Ldarga:
                            ilProcessor.InsertBeforeSeamlessly(ref inst, Instruction.Create(OpCodes.Call, refTile_GetDataMDef));
                            break;
                        case Code.Starg_S:
                        case Code.Starg:
                            throw new NotImplementedException();
                        default:
                            break;
                    }
                }
                if (methodsReferences.TryGetValue(currentMethodOldId, out var callers)) {
                    foreach (var caller in callers.Values) {
                        works.Push(caller);
                    }
                }

                return;
            }
        }
    }

    private void Adjust_RefFeature(ModFwModder modder, 
        Dictionary<string, HashSet<int>> modifiedTileParameters, 
        Dictionary<string, MethodDefinition> methodShouldAdjust,
        Dictionary<string, MethodDefinition> allMethods, 
        MethodDefinition[] tileOperateMethodsArray) {

        int progress = 0;
        foreach (var method in tileOperateMethodsArray) {

            if (!method.HasBody) {
                continue;
            }

            var jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);

            progress += 1;

            Console.WriteLine($"[{progress}/{methodShouldAdjust.Count}] Adjusting Tile null handling in method: {method.GetDebugName()}");

            var iLProcessor = method.Body.GetILProcessor();

            EachMethod_Analyze_WillModifyLocals(method, modifiedTileParameters, jumpTargets, out var notReadonlyVariables);

            Console.WriteLine($"Identified {notReadonlyVariables.Count} non-readonly TileData variables in method: {method.GetDebugName()} - {string.Join(", ", notReadonlyVariables.Select(x => "v" + x.Index))}");

            EachMethod_Adjust_MakeRefModifiedLocals(method, jumpTargets, notReadonlyVariables);

            void EachMethod_Adjust_StoreValueToAddress() {
                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    if (instruction.OpCode == OpCodes.Stind_Ref) {
                        var sourcePaths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)
                            .SelectMany(p => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, p.ParametersSources[0].Instructions.Last(), jumpTargets));

                        foreach (var path in sourcePaths) {

                            var loadBegin = path.Instructions.First();
                            TypeReference? type = null;
                            switch (loadBegin.OpCode.Code) {
                                case Code.Call:
                                case Code.Callvirt:
                                    var calleeRef = (MethodReference)loadBegin.Operand!;
                                    type = calleeRef.ReturnType;
                                    break;
                                case Code.Ldflda:
                                case Code.Ldsflda:
                                    var field = (FieldReference)loadBegin.Operand!;
                                    type = field.FieldType;
                                    break;
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                case Code.Ldarg_S:
                                case Code.Ldarg:
                                case Code.Ldarga_S:
                                case Code.Ldarga:
                                    if (MonoModCommon.IL.TryGetReferencedParameter(method, loadBegin, out var p)) {
                                        type = p.ParameterType;
                                    }
                                    break;
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                case Code.Ldloc_S:
                                case Code.Ldloc:
                                case Code.Ldloca_S:
                                case Code.Ldloca:
                                    if (MonoModCommon.IL.TryGetReferencedVariable(method, loadBegin, out var v)) {
                                        type = v.VariableType;
                                    }
                                    break;
                            }
                            if (type != null && IsTileType(type)) {
                                instruction.OpCode = OpCodes.Stobj;
                                instruction.Operand = tileTypeDef;
                                break;
                            }
                        }
                    }
                }
            }

            void EachMethod_Adjust_VariableDefinitionType() {
                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    switch (instruction.OpCode.Code) {
                        case Code.Stloc:
                        case Code.Stloc_S:
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:

                            var varRef = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                            if (!IsTileType(varRef.VariableType)) {
                                continue;
                            }

                            var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)
                                .SelectMany(p => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, p.ParametersSources[0].Instructions.Last(), jumpTargets))
                                .ToHashSet();

                            if (varRef.VariableType is ByReferenceType) {
                                foreach (var path in paths) {
                                    if (path.StackTopType is ByReferenceType) {
                                        continue;
                                    }

                                    var topInstruction = path.Instructions.First();
                                    switch (topInstruction.OpCode.Code) {
                                        case Code.Ldnull:
                                            topInstruction.OpCode = OpCodes.Call;
                                            topInstruction.Operand = tileTypeDef.Method("get_" + nameof(TileData.NULLREF));
                                            break;
                                        case Code.Ldarg_0:
                                        case Code.Ldarg_1:
                                        case Code.Ldarg_2:
                                        case Code.Ldarg_3:
                                        case Code.Ldarg_S:
                                        case Code.Ldarg:
                                            var parameter = MonoModCommon.IL.GetReferencedParameter(method, topInstruction);
                                            if (!IsTileType(parameter.ParameterType)) {
                                                break;
                                            }
                                            parameter.ParameterType = tileTypeDef.MakeByReferenceType();
                                            break;
                                        case Code.Ldloc_0:
                                        case Code.Ldloc_1:
                                        case Code.Ldloc_2:
                                        case Code.Ldloc_3:
                                        case Code.Ldloc_S:
                                        case Code.Ldloc:
                                            var variable = MonoModCommon.IL.GetReferencedVariable(method, topInstruction);
                                            variable.VariableType = tileTypeDef.MakeByReferenceType();
                                            break;
                                        case Code.Ldfld:
                                            topInstruction.OpCode = OpCodes.Ldflda;
                                            break;
                                        case Code.Ldsfld:
                                            topInstruction.OpCode = OpCodes.Ldsflda;
                                            break;
                                        case Code.Call:
                                        case Code.Callvirt:
                                            var calleeRef = (MethodReference)topInstruction.Operand!;
                                            if (calleeRef.Name == "InvokeCreate" && calleeRef.DeclaringType.FullName == "OTAPI.Hooks/Tile") {
                                                topInstruction.OpCode = OpCodes.Call;
                                                if (calleeRef.Parameters.Count == 0) {
                                                    topInstruction.OpCode = OpCodes.Call;
                                                    topInstruction.Operand = tileTypeDef.Method("get_" + nameof(TileData.EMPTYREF));
                                                }
                                                else {
                                                    topInstruction.OpCode = OpCodes.Call;
                                                    topInstruction.Operand = tileTypeDef.Methods.First(m =>
                                                    m.Parameters.Count == calleeRef.Parameters.Count &&
                                                    m.Name == nameof(TileData.GetEMPTYREF));
                                                }
                                            }
                                            break;
                                    }
                                }
                            }
                            else {
                                HashSet<Instruction> visited = new HashSet<Instruction>();

                                if (paths.Count == 1) {
                                    var path = paths.First();
                                    if (path.Instructions.Length == 1 && path.Instructions[0].OpCode == OpCodes.Call) {
                                        var call = (MethodReference)path.Instructions[0].Operand!;
                                        if (call.GetIdentifier() == tileTypeDef.Method("get_" + nameof(TileData.NULLREF)).GetIdentifier()) {
                                            path.Instructions[0].OpCode = OpCodes.Ldsfld;
                                            path.Instructions[0].Operand = tileTypeDef.Fields.Single(f => f.Name == nameof(TileData.NULL));
                                            break;
                                        }
                                    }
                                }

                                foreach (var path in paths) {
                                    if (path.StackTopType is ByReferenceType referenceType) {
                                        var loadEnd = path.Instructions.Last();
                                        if (visited.Add(loadEnd)) {

                                            List<Instruction> inserts = new List<Instruction>();

                                            if (loadEnd.OpCode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineSwitch) {
                                                var loadObj = Instruction.Create(OpCodes.Ldobj, referenceType.ElementType);

                                                inserts.Add(Instruction.Create(OpCodes.Br, loadEnd.Next));
                                                inserts.Add(loadObj);
                                                if (loadEnd.Operand is Instruction target) {
                                                    inserts.Add(Instruction.Create(OpCodes.Br, target));
                                                    loadEnd.Operand = loadObj;
                                                }
                                                else if (loadEnd.Operand is Instruction[] targets) {
                                                    inserts.Add(Instruction.Create(OpCodes.Switch, targets));
                                                    for (int i = 0; i < targets.Length; i++) {
                                                        if (targets[i] == instruction) {
                                                            targets[i] = loadObj;
                                                        }
                                                    }
                                                }

                                                // update jump targets
                                                jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);
                                            }
                                            else {
                                                inserts.Add(Instruction.Create(OpCodes.Ldobj, referenceType.ElementType));
                                            }

                                            iLProcessor.InsertAfter(loadEnd, inserts);
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            void EachMethod_Adjust_NullCheck() {
                void NullLoad(Instruction insertedNotNullCheck) {
                    var argPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, insertedNotNullCheck, jumpTargets);
                    foreach (var argPath in argPaths) {
                        var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, argPath.ParametersSources[0].Instructions.Last(), jumpTargets);
                        foreach (var path in paths) {
                            if (path.StackTopType is null) {
                                foreach (var inst in path.Instructions) {
                                    if (inst.OpCode == OpCodes.Ldnull) {
                                        inst.OpCode = OpCodes.Ldsfld;
                                        inst.Operand = tileTypeDef.Fields.Single(f => f.Name == nameof(TileData.NULL));
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    switch (instruction.OpCode.Code) {
                        // Jump if v1 is true
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S: {
                                var path = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets).First();
                                var type = MonoModCommon.Stack.AnalyzeStackTopType(method, path.ParametersSources[0].Instructions.Last(), jumpTargets);
                                if (type is not null && IsTileType(type)) {
                                    var isNotNullCall = Instruction.Create(OpCodes.Call, tileTypeDef.FindMethod("get_" + nameof(TileData.IsNotNull)));
                                    iLProcessor.InsertBefore(instruction, isNotNullCall);
                                    NullLoad(isNotNullCall);
                                }
                                break;
                            }
                        // True if v1 equals v2
                        case Code.Ceq: {
                                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                                    var typeV1 = MonoModCommon.Stack.AnalyzeStackTopType(method, path.ParametersSources[0].Instructions.Last(), jumpTargets);
                                    var typeV2 = MonoModCommon.Stack.AnalyzeStackTopType(method, path.ParametersSources[1].Instructions.Last(), jumpTargets);

                                    if ((typeV1 is null && typeV2 is not null && IsTileType(typeV2))) {
                                        foreach (var instV1 in path.ParametersSources[0].Instructions) {
                                            instV1.OpCode = OpCodes.Nop;
                                            instV1.Operand = null;
                                        }
                                        instruction.OpCode = OpCodes.Call;
                                        instruction.Operand = tileTypeDef.FindMethod("get_" + nameof(TileData.IsNull));
                                        NullLoad(instruction);
                                    }
                                    else if (typeV2 is null && typeV1 is not null && IsTileType(typeV1)) {
                                        foreach (var instV2 in path.ParametersSources[1].Instructions) {
                                            instV2.OpCode = OpCodes.Nop;
                                            instV2.Operand = null;
                                        }
                                        instruction.OpCode = OpCodes.Call;
                                        instruction.Operand = tileTypeDef.FindMethod("get_" + nameof(TileData.IsNull));
                                        NullLoad(instruction);
                                    }
                                }
                                break;
                            }
                        // True if v1 is greater than v2
                        case Code.Cgt_Un: {
                                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                                    var typeV1 = MonoModCommon.Stack.AnalyzeStackTopType(method, path.ParametersSources[0].Instructions.Last(), jumpTargets);
                                    var typeV2 = MonoModCommon.Stack.AnalyzeStackTopType(method, path.ParametersSources[1].Instructions.Last(), jumpTargets);
                                    if (typeV2 is null && typeV1 is not null && IsTileType(typeV1)) {
                                        foreach (var instV2 in path.ParametersSources[1].Instructions) {
                                            instV2.OpCode = OpCodes.Nop;
                                            instV2.Operand = null;
                                        }
                                        instruction.OpCode = OpCodes.Call;
                                        instruction.Operand = tileTypeDef.FindMethod("get_" + nameof(TileData.IsNotNull));
                                        NullLoad(instruction);
                                    }
                                }
                                break;
                            }
                    }
                }
            }

            void EachMethod_Adjust_LoadAddress() {

                foreach (var instruction in method.Body.Instructions.ToArray()) {

                    HashSet<MonoModCommon.Stack.StackTopTypePath> workPaths = new();
                    HashSet<MonoModCommon.Stack.StackTopTypePath> visited = new();

                    int sourceIndexOfLoadTileRef = -1;
                    switch (instruction.OpCode.Code) {
                        case Code.Callvirt:
                        case Code.Call:
                        case Code.Newobj: {
                                var calleeRef = (MethodReference)instruction.Operand!;

                                if (allMethods.TryGetValue(calleeRef.GetIdentifier(), out var calleeDef)) {

                                    if (!calleeDef.Parameters.Any(p => IsTileType(p.ParameterType))) {

                                        if (!IsTileType(calleeDef.DeclaringType) || !calleeDef.HasThis) {
                                            break;
                                        }

                                        if (instruction.OpCode == OpCodes.Newobj) {
                                            break;
                                        }
                                    }

                                    var methodCallPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets);

                                    if (modifiedTileParameters.TryGetValue(calleeDef.GetIdentifier(), out var theseParametersWillBeEdit)) {
                                        foreach (var methodCallPath in methodCallPaths) {
                                            foreach (var pIndex in theseParametersWillBeEdit) {
                                                var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                                    method,
                                                    methodCallPath.ParametersSources[pIndex].Instructions.Last(),
                                                    jumpTargets);

                                                foreach (var path in paths) {
                                                    workPaths.Add(path);
                                                }
                                            }
                                        }
                                    }

                                    theseParametersWillBeEdit ??= [];

                                    // When calling an instance method of a struct, the 'this' parameter needs to load the reference address, not the value itself
                                    if (calleeDef.HasThis && instruction.OpCode != OpCodes.Newobj && IsTileType(calleeDef.DeclaringType)) {

                                        // Avoid repeating the previous logic
                                        if (theseParametersWillBeEdit.Contains(0)) {
                                            break;
                                        }

                                        foreach (var methodCallPath in methodCallPaths) {
                                            var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                                method,
                                                methodCallPath.ParametersSources[0].Instructions.Last(),
                                                jumpTargets);

                                            foreach (var path in paths) {
                                                workPaths.Add(path);
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                        case Code.Stloc: {
                                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                                if (variable.VariableType is ByReferenceType referenceType && IsTileType(referenceType.ElementType)) {
                                    sourceIndexOfLoadTileRef = 0;
                                }
                                break;
                            }
                        case Code.Ldfld: {
                                var field = (FieldReference)instruction.Operand!;
                                if (IsTileType(field.DeclaringType)) {
                                    sourceIndexOfLoadTileRef = 0;
                                }
                                break;
                            }
                        case Code.Stsfld: {
                                var field = (FieldReference)instruction.Operand!;
                                if (field.FieldType is ByReferenceType referenceType && IsTileType(referenceType.ElementType)) {
                                    sourceIndexOfLoadTileRef = 0;
                                }
                                break;
                            }
                        case Code.Stfld: {
                                var field = (FieldReference)instruction.Operand!;
                                if (IsTileType(field.DeclaringType)) {
                                    sourceIndexOfLoadTileRef = 0;
                                }
                                else if (field.FieldType is ByReferenceType referenceType && IsTileType(referenceType.ElementType)) {
                                    sourceIndexOfLoadTileRef = 1;
                                }
                                break;
                            }
                        case Code.Initobj:
                        case Code.Ldobj: {
                                var type = (TypeReference)instruction.Operand!;
                                if (IsTileType(type)) {
                                    sourceIndexOfLoadTileRef = 0;
                                }
                                break;
                            }
                    }

                    if (sourceIndexOfLoadTileRef != -1) {
                        foreach (var executePath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                            var loadTileRef = executePath.ParametersSources[sourceIndexOfLoadTileRef].Instructions.Last();

                            var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                method,
                                loadTileRef,
                                jumpTargets);

                            foreach (var path in paths) {
                                workPaths.Add(path);
                            }
                        }
                    }

                    foreach (var loadPath in workPaths) {

                        if (visited.Add(loadPath)) {

                            var loadBegin = loadPath.Instructions.First();
                            var loadEnd = loadPath.Instructions.Last();

                            switch (loadBegin.OpCode.Code) {
                                case Code.Ldnull:
                                    loadBegin.OpCode = OpCodes.Call;
                                    loadBegin.Operand = tileTypeDef.Method("get_" + nameof(TileData.NULLREF));
                                    break;
                                case Code.Call:
                                case Code.Callvirt:
                                case Code.Ldfld:
                                case Code.Ldsfld:

                                    if (workPaths.Count == 1 && (loadEnd.OpCode == OpCodes.Dup || MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, loadBegin).Length == 1)) {
                                        loadEnd = loadBegin;
                                    }

                                    var calleeRef = loadBegin.Operand as MethodReference;
                                    var loadField = loadBegin.Operand as FieldReference;

                                    if (loadField is not null && loadField.DeclaringType.FullName == tileTypeDef.FullName && loadField.Name == nameof(TileData.NULL)) {
                                        loadBegin.OpCode = OpCodes.Call;
                                        loadBegin.Operand = tileTypeDef.Method("get_" + nameof(TileData.NULLREF));
                                    }
                                    else if (loadField is not null && !loadField.Resolve().IsInitOnly) {
                                        if (loadBegin.OpCode == OpCodes.Ldfld) {
                                            loadBegin.OpCode = OpCodes.Ldflda;
                                        }
                                        else if (loadBegin.OpCode == OpCodes.Ldsfld) {
                                            loadBegin.OpCode = OpCodes.Ldsflda;
                                        }
                                    }
                                    else if (loadField is not null || (calleeRef is not null && calleeRef.ReturnType is not ByReferenceType)) {
                                        var variable = new VariableDefinition(tileTypeDef);
                                        method.Body.Variables.Add(variable);
                                        var setLocal = MonoModCommon.IL.BuildVariableStore(method, method.Body, variable);
                                        var loadLocalAddress = MonoModCommon.IL.BuildVariableLoadAddress(method, method.Body, variable);

                                        List<Instruction> inserts = new List<Instruction>();

                                        if (loadEnd.OpCode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineSwitch) {

                                            inserts.Add(Instruction.Create(OpCodes.Br, loadEnd.Next));
                                            inserts.Add(setLocal);
                                            inserts.Add(loadLocalAddress);
                                            if (loadEnd.Operand is Instruction target) {
                                                inserts.Add(Instruction.Create(OpCodes.Br, target));
                                                loadEnd.Operand = setLocal;
                                            }
                                            else if (loadEnd.Operand is Instruction[] targets) {
                                                inserts.Add(Instruction.Create(OpCodes.Switch, targets));
                                                for (int i = 0; i < targets.Length; i++) {
                                                    if (targets[i] == instruction) {
                                                        targets[i] = setLocal;
                                                    }
                                                }
                                            }

                                            // update jump targets
                                            jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);
                                        }
                                        else {
                                            inserts.Add(setLocal);
                                            inserts.Add(loadLocalAddress);
                                        }

                                        iLProcessor.InsertAfter(loadEnd, inserts);
                                    }
                                    break;
                                case Code.Ldarg:
                                    if (MonoModCommon.IL.TryGetReferencedParameter(method, loadBegin, out var p)) {
                                        if (p.ParameterType is not ByReferenceType) {
                                            loadBegin.OpCode = OpCodes.Ldarga;
                                            loadBegin.Operand = p;
                                        }
                                    }
                                    break;
                                case Code.Ldarg_S:
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                    if (MonoModCommon.IL.TryGetReferencedParameter(method, loadBegin, out var p_s)) {
                                        if (p_s.ParameterType is not ByReferenceType) {
                                            loadBegin.OpCode = OpCodes.Ldarga_S;
                                            loadBegin.Operand = p_s;
                                        }
                                    }
                                    break;

                                case Code.Ldloc:
                                    if (MonoModCommon.IL.TryGetReferencedVariable(method, loadBegin, out var v)) {
                                        if (v.VariableType is not ByReferenceType) {
                                            loadBegin.OpCode = OpCodes.Ldloca;
                                            loadBegin.Operand = v;
                                        }
                                    }
                                    break;
                                case Code.Ldloc_S:
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                    if (MonoModCommon.IL.TryGetReferencedVariable(method, loadBegin, out var v_s)) {
                                        if (v_s.VariableType is not ByReferenceType) {
                                            loadBegin.OpCode = OpCodes.Ldloca_S;
                                            loadBegin.Operand = v_s;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }

            void EachMethod_Adjust_LoadObjValue() {
                foreach (var instruction in method.Body.Instructions.ToArray()) {

                    HashSet<MonoModCommon.Stack.StackTopTypePath> workPaths = new();
                    HashSet<MonoModCommon.Stack.StackTopTypePath> visited = new();

                    int index = -1;
                    switch (instruction.OpCode.Code) {
                        case Code.Callvirt:
                        case Code.Call: {

                                var calleeRef = (MethodReference)instruction.Operand!;

                                if (allMethods.TryGetValue(calleeRef.GetIdentifier(), out var calleeDef)) {

                                    if (!calleeDef.Parameters.Any(p => IsTileType(p.ParameterType)) && !(IsTileType(calleeDef.DeclaringType) && calleeDef.HasThis)) {
                                        break;
                                    }

                                    var methodCallPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets);

                                    modifiedTileParameters.TryGetValue(calleeDef.GetIdentifier(), out var theseParametersWillBeEdit);
                                    theseParametersWillBeEdit ??= [];
                                    for (var i = 0; i < calleeDef.Parameters.Count; i++) {
                                        var pIndex = i + (calleeDef.HasThis ? 1 : 0);

                                        if (IsTileType(calleeDef.Parameters[i].ParameterType) && !(theseParametersWillBeEdit?.Contains(pIndex) ?? false)) {

                                            foreach (var methodCallPath in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets)) {
                                                var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                                    method,
                                                    methodCallPath.ParametersSources[pIndex].Instructions.Last(),
                                                    jumpTargets);

                                                foreach (var path in paths) {
                                                    workPaths.Add(path);
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                        case Code.Stloc: {
                                var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                                if (variable.VariableType is not ByReferenceType && IsTileType(variable.VariableType)) {
                                    // 0. push value
                                    index = 0;
                                }
                                break;
                            }
                        case Code.Starg:
                        case Code.Starg_S: {
                                var parameter = MonoModCommon.IL.GetReferencedParameter(method, instruction);
                                if (parameter.ParameterType is not ByReferenceType && IsTileType(parameter.ParameterType)) {
                                    // 0. push value
                                    index = 0;
                                }
                                break;
                            }
                        case Code.Stsfld: {
                                var field = (FieldReference)instruction.Operand!;
                                if (field.FieldType is not ByReferenceType referenceType && IsTileType(field.FieldType)) {
                                    // 0. push value
                                    index = 0;
                                }
                                break;
                            }
                        case Code.Stfld: {
                                var field = (FieldReference)instruction.Operand!;
                                if (field.FieldType is not ByReferenceType referenceType && IsTileType(field.FieldType)) {
                                    // 0. push instance
                                    // 1. push value
                                    index = 1;
                                }
                                break;
                            }
                        case Code.Stobj: {
                                var type = (TypeReference)instruction.Operand!;
                                if (IsTileType(type)) {
                                    // 0. push address
                                    // 1. push value
                                    index = 1;
                                }
                                break;
                            }
                    }

                    if (index != -1) {
                        foreach (var fieldPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                            var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                method,
                                fieldPath.ParametersSources[index].Instructions.Last(),
                                jumpTargets);
                            foreach (var path in paths) {
                                workPaths.Add(path);
                            }
                        }
                    }

                    foreach (var loadPath in workPaths) {
                        if (visited.Add(loadPath)) {

                            var loadBegin = loadPath.Instructions.First();
                            var loadEnd = loadPath.Instructions.Last();

                            bool shouldLdObj = false;

                            switch (loadBegin.OpCode.Code) {
                                case Code.Ldnull:
                                    loadBegin.OpCode = OpCodes.Ldsfld;
                                    loadBegin.Operand = tileTypeDef.Fields.Single(f => f.Name == nameof(TileData.NULL));
                                    break;
                                case Code.Call:
                                case Code.Callvirt:
                                    var calleeRef = (MethodReference)loadBegin.Operand!;
                                    if (calleeRef.ReturnType is ByReferenceType) {

                                        if (calleeRef.DeclaringType.FullName == tileTypeDef.FullName && calleeRef.Name == "get_" + nameof(TileData.NULLREF)) {
                                            loadBegin.OpCode = OpCodes.Ldsfld;
                                            loadBegin.Operand = tileTypeDef.Fields.Single(f => f.Name == nameof(TileData.NULL));
                                        }
                                        else {
                                            shouldLdObj = true;
                                        }
                                    }
                                    break;
                                case Code.Ldarg:
                                case Code.Ldarg_S:
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                    if (MonoModCommon.IL.TryGetReferencedParameter(method, loadBegin, out var p)) {
                                        if (p.ParameterType is ByReferenceType) {
                                            shouldLdObj = true;
                                        }
                                    }
                                    break;

                                case Code.Ldloc:
                                case Code.Ldloc_S:
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                    if (MonoModCommon.IL.TryGetReferencedVariable(method, loadBegin, out var v)) {
                                        if (v.VariableType is ByReferenceType) {
                                            shouldLdObj = true;
                                        }
                                    }
                                    break;
                            }

                            if (shouldLdObj) {

                                if (loadEnd.OpCode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineSwitch) {
                                    var loadObj = Instruction.Create(OpCodes.Ldobj, tileTypeDef);

                                    List<Instruction> inserts = [];

                                    inserts.Add(Instruction.Create(OpCodes.Br, loadEnd.Next));
                                    inserts.Add(loadObj);

                                    if (loadEnd.Operand is Instruction target) {
                                        inserts.Add(Instruction.Create(OpCodes.Br, target));
                                        loadEnd.Operand = loadObj;
                                    }
                                    else if (loadEnd.Operand is Instruction[] targets) {
                                        inserts.Add(Instruction.Create(OpCodes.Switch, targets));
                                        for (int i = 0; i < targets.Length; i++) {
                                            if (targets[i] == instruction) {
                                                targets[i] = loadObj;
                                            }
                                        }
                                    }
                                    iLProcessor.InsertAfter(loadEnd, inserts);
                                }
                                else {
                                    iLProcessor.InsertAfter(loadEnd, Instruction.Create(OpCodes.Ldobj, tileTypeDef));
                                }
                            }
                        }
                    }
                }
            }

            void EachMethod_Adjust_LockObj() {
                foreach (var instructionEnterCheck in method.Body.Instructions.ToArray()) {
                    if (instructionEnterCheck.OpCode != OpCodes.Call) {
                        continue;
                    }
                    var calleeRef_Monitor_Enter = (MethodReference)instructionEnterCheck.Operand!;
                    if (calleeRef_Monitor_Enter.DeclaringType.FullName != typeof(System.Threading.Monitor).FullName ||
                        calleeRef_Monitor_Enter.Name != nameof(System.Threading.Monitor.Enter)) {
                        continue;
                    }
                    var paramPath_Monitor_Enter = MonoModCommon.Stack.AnalyzeParametersSources(method, instructionEnterCheck, jumpTargets);
                    if (paramPath_Monitor_Enter.Length != 1) {
                        throw new NotSupportedException();
                    }

                    var loadObjInstruction_Enter = paramPath_Monitor_Enter[0].ParametersSources[0].Instructions.First();
                    var loadRefFlagInstruction_Enter = paramPath_Monitor_Enter[0].ParametersSources[1].Instructions.First();

                    if (!MonoModCommon.IL.TryGetReferencedVariable(method, loadObjInstruction_Enter, out var variable_tile)) {
                        throw new NotSupportedException();
                    }

                    if (!IsTileType(variable_tile.VariableType)) {
                        continue;
                    }

                    if (!MonoModCommon.IL.TryGetReferencedVariable(method, loadRefFlagInstruction_Enter, out var variable_refFlag)) {
                        throw new NotSupportedException();
                    }

                    var variable_lock = new VariableDefinition(modder.Module.TypeSystem.Object);
                    method.Body.Variables.Add(variable_lock);

                    // insert lock stloc
                    foreach (var instructionEnterSet in method.Body.Instructions.ToArray()) {
                        switch (instructionEnterSet.OpCode.Code) {
                            case Code.Stloc_0:
                            case Code.Stloc_1:
                            case Code.Stloc_2:
                            case Code.Stloc_3:
                            case Code.Stloc_S:
                            case Code.Stloc:
                                break;
                            default:
                                continue;
                        }
                        if (!MonoModCommon.IL.TryGetReferencedVariable(method, instructionEnterSet, out var setVariable)) {
                            continue;
                        }
                        if (setVariable != variable_tile) {
                            continue;
                        }
                        iLProcessor.InsertAfter(instructionEnterSet, [
                            MonoModCommon.IL.BuildVariableLoadAddress(method, method.Body, variable_tile),
                            Instruction.Create(OpCodes.Call, tileTypeDef.Method(nameof(TileData.GetLock))),
                            MonoModCommon.IL.BuildVariableStore(method, method.Body, variable_lock),
                        ]);
                    }

                    var loadLock = MonoModCommon.IL.BuildVariableLoad(method, method.Body, variable_lock);

                    foreach (var instructionLoadObj in method.Body.Instructions.ToArray()) {
                        switch (instructionLoadObj.OpCode.Code) {
                            case Code.Ldloc_0:
                            case Code.Ldloc_1:
                            case Code.Ldloc_2:
                            case Code.Ldloc_3:
                            case Code.Ldloc_S:
                            case Code.Ldloc:
                                break;
                            default:
                                continue;
                        }
                        if (!MonoModCommon.IL.TryGetReferencedVariable(method, instructionLoadObj, out var loadVariable)) {
                            continue;
                        }
                        if (loadVariable != variable_tile) {
                            continue;
                        }
                        instructionLoadObj.OpCode = loadLock.OpCode;
                        instructionLoadObj.Operand = loadLock.Operand;
                    }
                }
            }

            EachMethod_Adjust_StoreValueToAddress();
            EachMethod_Adjust_VariableDefinitionType();
            EachMethod_Adjust_NullCheck();
            EachMethod_Adjust_LoadAddress();
            EachMethod_Adjust_LoadObjValue();
            EachMethod_Adjust_VariableDefinitionType();
            EachMethod_Adjust_LockObj();
        }
    }

    private void EachMethod_Adjust_MakeRefModifiedLocals(MethodDefinition method, 
        Dictionary<Instruction, List<Instruction>> jumpTargets, 
        HashSet<VariableDefinition> notReadonlyVariables) {

        bool anyVariableEdit = false;
        if (notReadonlyVariables.Count > 0) {
            do {
                anyVariableEdit = false;
                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    switch (instruction.OpCode.Code) {
                        case Code.Stloc:
                        case Code.Stloc_S:
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                            var varRef = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                            if (varRef.VariableType is ByReferenceType || !IsTileType(varRef.VariableType)) {
                                continue;
                            }

                            var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)
                                .SelectMany(p => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, p.ParametersSources[0].Instructions.Last(), jumpTargets));

                            if (paths.Any(p => p.StackTopType is ByReferenceType)) {

                                Console.WriteLine($"    Modified variable v{varRef.Index} to TileData&");

                                var varDef = varRef.Resolve();

                                if (notReadonlyVariables.Contains(varDef)) {
                                    varDef.VariableType = tileTypeDef.MakeByReferenceType();
                                    anyVariableEdit = true;
                                }
                            }
                            break;
                    }
                }
            }
            while (anyVariableEdit);
        }
    }

    private void EachMethod_Analyze_WillModifyLocals(MethodDefinition method, 
        Dictionary<string, HashSet<int>> modifiedTileParameters, 
        Dictionary<Instruction, List<Instruction>> jumpTargets,
        out HashSet<VariableDefinition> notReadonlyVariables) {

        notReadonlyVariables = [];

        foreach (var instruction in method.Body.Instructions.ToArray()) {
            if (instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldReference tileField && IsTileType(tileField.DeclaringType)) {
                var argPaths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets);
                foreach (var argPath in argPaths) {
                    var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, argPath.ParametersSources[0].Instructions.Last(), jumpTargets);
                    foreach (var path in paths) {
                        if (MonoModCommon.IL.TryGetReferencedVariable(method, path.Instructions.First(), out var variable)) {
                            notReadonlyVariables.Add(variable);
                        }
                    }
                }
            }
            else if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference tileMethod &&
                modifiedTileParameters.TryGetValue(tileMethod.GetIdentifier(), out var theseParametersWillBeEdit)) {

                var paramPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets);
                foreach (var paramPath in paramPaths) {
                    for (int i = 0; i < paramPath.ParametersSources.Length; i++) {
                        if (!theseParametersWillBeEdit.Contains(i)) {
                            continue;
                        }

                        var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, paramPath.ParametersSources[i].Instructions.Last(), jumpTargets);
                        foreach (var path in paths) {
                            if (MonoModCommon.IL.TryGetReferencedVariable(method, path.Instructions.First(), out var variable)) {
                                notReadonlyVariables.Add(variable);
                            }
                        }
                    }
                }
            }
        }
    }

    private void Adjust_RelinkModifiedComponets(ModFwModder modder, 
        Dictionary<string, HashSet<int>> modifiedTileParameters,
        Dictionary<string, MethodDefinition> allMethods,
        Dictionary<string, FieldDefinition> fieldShouldAdjust, 
        Dictionary<string, MethodDefinition> methodShouldAdjust,
        out Dictionary<string, Dictionary<string, MethodDefinition>> fieldReferences,
        out Dictionary<string, Dictionary<string, MethodDefinition>> methodsReferences) {

        fieldReferences = new();
        methodsReferences = new();

        var oldTileFullName = tileTypeOldDef.FullName;
        // relink method calls and thisfieldReference
        foreach (var type in modder.Module.GetAllTypes()) {
            foreach (var method in type.Methods) {
                if (!method.HasBody) {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions.ToArray()) {
                    if (instruction.Operand is MethodReference calleeRef) {

                        if (calleeRef.DeclaringType.FullName == oldTileFullName ||
                            calleeRef.ReturnType.FullName == oldTileFullName ||
                            calleeRef.Parameters.Any(
                                p => p.ParameterType.FullName == oldTileFullName ||
                                (p.ParameterType is ByReferenceType referenceType && referenceType.ElementType.FullName == oldTileFullName))) {

                            var declaringType = calleeRef.DeclaringType;
                            if (calleeRef.DeclaringType.FullName == oldTileFullName) {
                                declaringType = tileTypeDef;
                            }

                            HashSet<int> shouldBeReferenceExculdeThis = [];

                            if (modifiedTileParameters.TryGetValue(calleeRef.GetIdentifier(), out var byRefParamIndexesInculdeThis)) {
                                foreach (var indexInculdeThis in byRefParamIndexesInculdeThis) {
                                    var paramIndex = indexInculdeThis;
                                    if (calleeRef.HasThis && instruction.OpCode != OpCodes.Newobj) {
                                        paramIndex -= 1;
                                    }
                                    if (paramIndex != -1) {
                                        shouldBeReferenceExculdeThis.Add(paramIndex);
                                    }
                                }
                            }

                            if (allMethods.TryGetValue(calleeRef.GetIdentifier(true, tileNameMap, shouldBeReferenceExculdeThis), out var methodDefinition)) {
                                instruction.Operand = MonoModCommon.Structure.DeepMapMethodReference(methodDefinition, new());
                            }
                            else {
                                Console.WriteLine($"[Waring] could not find method {calleeRef.GetIdentifier(true, tileNameMap, shouldBeReferenceExculdeThis)} in {declaringType.FullName}");
                            }
                        }
                        else if (calleeRef.Parameters.Any(p => p.ParameterType.FullName == tileCollectionDefOld.FullName) ||
                            calleeRef.ReturnType.FullName == tileCollectionDefOld.FullName) {

                            var declaringType = calleeRef.DeclaringType;

                            if (allMethods.TryGetValue(calleeRef.GetIdentifier(true, tileNameMap), out var methodDefinition)) {
                                instruction.Operand = MonoModCommon.Structure.DeepMapMethodReference(methodDefinition, new());
                            }
                        }
                    }
                    else if (instruction.Operand is FieldReference field) {
                        if (fieldShouldAdjust.TryGetValue(field.GetIdentifier(), out var fieldDefinition)) {
                            instruction.Operand = fieldDefinition;
                        }
                    }
                }
            }
        }


        foreach (var type in modder.Module.GetAllTypes()) {
            foreach (var method in type.Methods) {

                if (!method.HasBody) {
                    continue;
                }

                var jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);

                if (method.Parameters.Any(x => IsTileType(x.ParameterType))) {
                    methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                }
                if (method.Body.Variables.Any(x => IsTileType(x.VariableType))) {
                    methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                }

                var iLProcessor = method.Body.GetILProcessor();

                foreach (var instruction in method.Body.Instructions.ToArray()) {

                    if (!methodShouldAdjust.ContainsKey(method.GetIdentifier())) {
                        if (instruction.Operand is IMemberDefinition member && member.DeclaringType is not null && IsTileType(member.DeclaringType)) {
                            methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                        }
                        else if (instruction.Operand is MethodReference methodRef) {
                            if (IsTileType(methodRef.DeclaringType) || methodRef.Parameters.Any(x => IsTileType(x.ParameterType))) {
                                methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                            }
                        }
                    }

                    if (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Newobj) {
                        var callee = (MethodReference)instruction.Operand!;
                        var calleeId = callee.GetIdentifier();
                        if (!methodsReferences.TryGetValue(calleeId, out var whoCalls)) {
                            methodsReferences[calleeId] = whoCalls = [];
                        }
                        whoCalls.TryAdd(method.GetIdentifier(), method);

                        if (callee.Parameters.Any(x => IsTileType(x.ParameterType))) {

                            var paths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets);

                            foreach (var path in paths) {
                                var parameterPreparations = path.ParametersSources;
                                for (int i = 0; i < parameterPreparations.Length; i++) {
                                    if (i == 0 && instruction.OpCode == OpCodes.Newobj) {
                                        continue;
                                    }
                                    var preparation = parameterPreparations[i];
                                    if (IsTileType(preparation.Parameter.ParameterType)) {
                                        if (preparation.Instructions.Length == 1) {
                                            var preparationIL = preparation.Instructions[0];
                                            if (preparationIL.OpCode == OpCodes.Ldnull) {
                                                preparationIL.OpCode = OpCodes.Ldsfld;
                                                preparationIL.Operand = tileTypeDef.Fields.Single(f => f.Name == nameof(TileData.NULL));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (instruction.Operand is FieldReference tileField) {
                        var tileFieldId = tileField.GetIdentifier();
                        if (!fieldReferences.TryGetValue(tileFieldId, out var thisfieldReference)) {
                            fieldReferences[tileFieldId] = thisfieldReference = [];
                        }
                        thisfieldReference.TryAdd(method.GetIdentifier(), method);

                        if (tileField.DeclaringType.FullName == "Terraria.Main" && tileField.Name == "tile") {
                            if (instruction.OpCode == OpCodes.Stsfld) {
                                instruction.Previous.OpCode = OpCodes.Call;
                                instruction.Previous.Operand = tileCollection_CreateMDef;
                            }
                            instruction.Operand = tileCollectionFieldDefInMain;
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Newobj) {
                        var calleeRef = (MethodReference)instruction.Operand!;
                        if (calleeRef.DeclaringType.FullName == typeof(Terraria.Tile).FullName) {
                            if (calleeRef.Parameters.Count == 0) {
                                instruction.OpCode = OpCodes.Call;
                                instruction.Operand = tileCreate;
                            }
                            else if (calleeRef.Parameters.Count == 1) {
                                instruction.OpCode = OpCodes.Call;
                                instruction.Operand = tileCreateWithExistingTile;
                            }
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call) {
                        var calleeRef = (MethodReference)instruction.Operand!;

                        if (calleeRef.DeclaringType.FullName == tileCollectionDefOld.FullName || calleeRef.DeclaringType.FullName == tileCollectionDef.FullName) {
                            if (calleeRef.Name == "get_Item") {
                                methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                            }
                            else if (calleeRef.Name == "set_Item") {

                                methodShouldAdjust.TryAdd(method.GetIdentifier(), method);

                                var local = new VariableDefinition(tileTypeDef);
                                method.Body.Variables.Add(local);

                                var setLocal = MonoModCommon.IL.BuildVariableStore(method, method.Body, local);

                                instruction.OpCode = setLocal.OpCode;
                                instruction.Operand = setLocal.Operand;

                                iLProcessor.InsertAfter(instruction, [
                                    Instruction.Create(OpCodes.Callvirt, tileCollection_getItemMDef),
                                    Instruction.Create(OpCodes.Ldloc, local),
                                    Instruction.Create(OpCodes.Stobj, tileTypeDef),
                                ]);
                            }
                        }
                        else if (calleeRef.DeclaringType.FullName == oldTileFullName) {

                            if (calleeRef.Name.StartsWith("get_")) {
                                var fieldName = calleeRef.Name.Substring(4);
                                instruction.OpCode = OpCodes.Ldfld;
                                instruction.Operand = tileTypeDef.FindField(fieldName) ?? throw new NotSupportedException($"Field {fieldName} not found");
                            }
                            else if (calleeRef.Name.StartsWith("set_")) {
                                var fieldName = calleeRef.Name.Substring(4);
                                instruction.OpCode = OpCodes.Stfld;
                                instruction.Operand = tileTypeDef.FindField(fieldName) ?? throw new NotSupportedException($"Field {fieldName} not found");
                            }
                            else {
                                instruction.OpCode = OpCodes.Call;
                                MethodReference? newerMethod = null;
                                foreach (var m in tileTypeDef.Methods) {
                                    if (m.Name != calleeRef.Name) {
                                        continue;
                                    }
                                    if (m.Parameters.Count != calleeRef.Parameters.Count) {
                                        continue;
                                    }
                                    for (int i = 0; i < m.Parameters.Count; i++) {
                                        if (m.Parameters[i].ParameterType.FullName != calleeRef.Parameters[i].ParameterType.FullName) {
                                            continue;
                                        }
                                    }
                                    newerMethod = m;
                                    break;
                                }
                                instruction.Operand = newerMethod ?? throw new NotSupportedException();
                            }
                        }
                    }
                }
            }
        }
    }

    private void Analyze_ComponentsNeedAdjust(ModFwModder modder,
        Dictionary<string, HashSet<int>> modifiedTileParameters, 
        out Dictionary<string, FieldDefinition> fieldShouldAdjust,
        out Dictionary<string, MethodDefinition> methodShouldAdjust) {
        fieldShouldAdjust = [];
        methodShouldAdjust = [];
        var oldTileFullName = tileTypeOldDef.FullName;

        foreach (var type in modder.Module.GetAllTypes()) {
            foreach (var field in type.Fields) {
                var fieldType = field.FieldType;
                if (fieldType.FullName == oldTileFullName) {
                    field.FieldType = tileTypeDef;
                    fieldShouldAdjust.TryAdd(field.GetIdentifier(), field);
                }
                if (fieldType.FullName == tileCollectionDefOld.FullName) {
                    field.FieldType = tileCollectionDef;
                    fieldShouldAdjust.TryAdd(field.GetIdentifier(), field);
                }
                if (fieldType.HasGenericParameters) {
                    var anyEdit = false;
                    foreach (var parameter in fieldType.GenericParameters) {
                        foreach (var c in parameter.Constraints) {
                            if (c.ConstraintType.FullName == oldTileFullName) {
                                c.ConstraintType = tileTypeDef;
                                anyEdit = true;
                            }
                        }
                    }
                    if (anyEdit) {
                        fieldShouldAdjust.TryAdd(field.GetIdentifier(), field);
                    }
                }
            }
            foreach (var prop in type.Properties) {
                if (prop.PropertyType.FullName == oldTileFullName) {
                    prop.PropertyType = tileTypeDef;
                }
                if (prop.PropertyType.FullName == tileCollectionDefOld.FullName) {
                    prop.PropertyType = tileCollectionDef;
                }
            }
            foreach (var method in type.Methods) {

                bool canAdd = false;

                if (method.ReturnType.FullName == oldTileFullName) {
                    method.ReturnType = tileTypeDef;
                    canAdd = true;
                }
                if (method.ReturnType.FullName == tileCollectionDefOld.FullName) {
                    method.ReturnType = tileCollectionDef;
                    canAdd = true;
                }

                if (method.HasGenericParameters) {
                    foreach (var parameter in method.GenericParameters) {
                        foreach (var c in parameter.Constraints) {
                            if (c.ConstraintType.FullName == oldTileFullName) {
                                c.ConstraintType = tileTypeDef;
                                canAdd = true;
                            }
                            if (c.ConstraintType.FullName == tileCollectionDefOld.FullName) {
                                c.ConstraintType = tileCollectionDef;
                                canAdd = true;
                            }
                        }
                    }
                }

                if (!modifiedTileParameters.TryGetValue(method.GetIdentifier(), out var byRefParamIndexesInculdeThis)) {
                    byRefParamIndexesInculdeThis = [];
                }

                foreach (var parameter in method.Parameters) {
                    bool paramIsTileRef = parameter.ParameterType is ByReferenceType referenceType && IsTileType(referenceType.ElementType);

                    if (parameter.ParameterType.FullName == oldTileFullName || paramIsTileRef) {
                        var index = method.Parameters.IndexOf(parameter);
                        var indexInculdeThis = index;
                        if (method.HasThis) {
                            indexInculdeThis += 1;
                        }
                        if (paramIsTileRef || byRefParamIndexesInculdeThis.Contains(indexInculdeThis)) {
                            method.Parameters[index].ParameterType = tileTypeDef.MakeByReferenceType();
                        }
                        else {
                            method.Parameters[index].ParameterType = tileTypeDef;
                        }
                        canAdd = true;
                    }
                    if (parameter.ParameterType.FullName == tileCollectionDefOld.FullName) {
                        parameter.ParameterType = tileCollectionDef;
                        canAdd = true;
                    }
                }

                if (method.HasBody) {
                    foreach (var variable in method.Body.Variables) {
                        if (variable.VariableType.FullName == oldTileFullName) {
                            variable.VariableType = tileTypeDef;
                            canAdd = true;
                        }
                    }
                    foreach (var variable in method.Body.Variables) {
                        if (variable.VariableType.FullName == tileCollectionDefOld.FullName) {
                            variable.VariableType = tileCollectionDefOld;
                            canAdd = true;
                        }
                    }

                    foreach (var instruction in method.Body.Instructions) {
                        switch (instruction.OpCode.Code) {
                            case Code.Call:
                            case Code.Callvirt:
                            case Code.Newobj:
                                var methodRef = (MethodReference)instruction.Operand;
                                if (IsTileType(methodRef.ReturnType)) {
                                    canAdd = true;
                                    break;
                                }
                                if (methodRef.Parameters.Any(x => IsTileType(x.ParameterType))) {
                                    canAdd = true;
                                    break;
                                }
                                break;
                            case Code.Ldsfld:
                            case Code.Ldsflda:
                            case Code.Ldfld:
                            case Code.Ldflda:
                                var fieldRef = (FieldReference)instruction.Operand;
                                if (IsTileType(fieldRef.FieldType)) {
                                    canAdd = true;
                                    break;
                                }
                                break;
                        }
                        if (canAdd) {
                            break;
                        }
                    }
                }

                if (canAdd) {
                    methodShouldAdjust.TryAdd(method.GetIdentifier(), method);
                }
            }
        }
    }

    private void Adjust_MethodReturnTileRef(ModFwModder modder) {
        int lastCount;
        Dictionary<string, MethodDefinition> retRefTileMethods = [];
        do {
            lastCount = retRefTileMethods.Count;

            foreach (var type in modder.Module.GetAllTypes()) {
                foreach (var method in type.Methods) {

                    if (!method.HasBody || method.ReturnType is ByReferenceType || !IsTileType(method.ReturnType)) {
                        continue;
                    }

                    var cachedJumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);

                    var retInstructions = method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();

                    Queue<MonoModCommon.Stack.StackTopTypePath> works = new();
                    HashSet<MonoModCommon.Stack.StackTopTypePath> visited = new();
                    var paths = retInstructions
                        .SelectMany(ret => MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, ret, cachedJumpTargets))
                        .SelectMany(path => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), cachedJumpTargets))
                        .ToHashSet();
                    foreach (var path in paths) {
                        works.Enqueue(path);
                    }
                    bool anyRefReturn = false;

                    while (!(works.Count == 0 || anyRefReturn)) {
                        var path = works.Dequeue();
                        visited.Add(path);

                        var topInstruction = path.Instructions.First();
                        switch (topInstruction.OpCode.Code) {
                            case Code.Call:
                            case Code.Callvirt: {
                                    var calleeRef = (MethodReference)topInstruction.Operand!;
                                    if (calleeRef.ReturnType is ByReferenceType referenceType && IsTileType(referenceType.ElementType)) {
                                        anyRefReturn = true;
                                    }
                                    if (retRefTileMethods.TryGetValue(calleeRef.GetIdentifier(), out var retRefTileMethod)) {
                                        topInstruction.Operand = retRefTileMethod;
                                        anyRefReturn = true;
                                    }
                                    break;
                                }
                            case Code.Ldarg_0:
                            case Code.Ldarg_1:
                            case Code.Ldarg_2:
                            case Code.Ldarg_3:
                            case Code.Ldarg_S:
                            case Code.Ldarg: {
                                    var parameter = MonoModCommon.IL.GetReferencedParameter(method, topInstruction);
                                    if (parameter.ParameterType is ByReferenceType referenceType && IsTileType(referenceType.ElementType)) {
                                        anyRefReturn = true;
                                    }
                                }
                                break;
                            case Code.Ldloc_0:
                            case Code.Ldloc_1:
                            case Code.Ldloc_2:
                            case Code.Ldloc_3:
                            case Code.Ldloc_S:
                            case Code.Ldloc:
                                var variable = MonoModCommon.IL.GetReferencedVariable(method, topInstruction);

                                foreach (var instruction in method.Body.Instructions.ToArray()) {
                                    if (instruction.OpCode.Code is
                                        Code.Stloc_0 or
                                        Code.Stloc_1 or
                                        Code.Stloc_2 or
                                        Code.Stloc_3 or
                                        Code.Stloc_S or
                                        Code.Stloc) {

                                        var checkVariable = MonoModCommon.IL.GetReferencedVariable(method, instruction);

                                        if (checkVariable == variable) {
                                            foreach (var sourcePath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, cachedJumpTargets)) {
                                                foreach (var variablePath in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, sourcePath.ParametersSources[0].Instructions.Last())) {
                                                    if (!visited.Contains(variablePath)) {
                                                        works.Enqueue(variablePath);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                break;
                        }
                    }

                    if (anyRefReturn) {
                        method.ReturnType = tileTypeDef.MakeByReferenceType();

                        retRefTileMethods.Add(method.GetIdentifier(), method);

                        foreach (var path in paths) {
                            if (path.StackTopType is ByReferenceType) {
                                continue;
                            }

                            var topInstruction = path.Instructions.First();
                            switch (topInstruction.OpCode.Code) {
                                case Code.Ldnull:
                                    topInstruction.OpCode = OpCodes.Call;
                                    topInstruction.Operand = tileTypeDef.Method("get_" + nameof(TileData.NULLREF));
                                    break;
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                case Code.Ldarg_S:
                                case Code.Ldarg:
                                    var parameter = MonoModCommon.IL.GetReferencedParameter(method, topInstruction);
                                    if (!IsTileType(parameter.ParameterType)) {
                                        break;
                                    }
                                    parameter.ParameterType = tileTypeDef.MakeByReferenceType();
                                    break;
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                case Code.Ldloc_S:
                                case Code.Ldloc:
                                    var variable = MonoModCommon.IL.GetReferencedVariable(method, topInstruction);
                                    variable.VariableType = tileTypeDef.MakeByReferenceType();
                                    break;
                                case Code.Ldfld:
                                    topInstruction.OpCode = OpCodes.Ldflda;
                                    break;
                                case Code.Ldsfld:
                                    topInstruction.OpCode = OpCodes.Ldsflda;
                                    break;
                                case Code.Call:
                                case Code.Callvirt:
                                    var calleeRef = (MethodReference)topInstruction.Operand!;
                                    if (calleeRef.Name == "InvokeCreate" && calleeRef.DeclaringType.FullName == "OTAPI.Hooks/Tile") {
                                        topInstruction.OpCode = OpCodes.Call;
                                        if (calleeRef.Parameters.Count == 0) {
                                            topInstruction.OpCode = OpCodes.Call;
                                            topInstruction.Operand = tileTypeDef.Method("get_" + nameof(TileData.EMPTYREF));
                                        }
                                        else {
                                            topInstruction.OpCode = OpCodes.Call;
                                            topInstruction.Operand = tileTypeDef.Methods.First(m =>
                                            m.Parameters.Count == calleeRef.Parameters.Count &&
                                            m.Name == nameof(TileData.GetEMPTYREF));
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }
        while (lastCount != retRefTileMethods.Count);

        Console.WriteLine($"Found {retRefTileMethods.Count} methods that return TileData&");
    }

    private void Adjust_MFWHMethods(ModFwModder modder, Dictionary<string, HashSet<int>> modifiedTileParameters) {
        foreach (var type in modder.Module.GetAllTypes()) {
            foreach (var method in type.Methods) {
                if (!method.Parameters.Any(p => IsTileType(p.ParameterType))) {
                    continue;
                }
                if (!method.HasBody) {
                    continue;
                }

                var jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);

                MethodReference? mfwhMethod = null;
                Instruction? mfwhCallInstruction = null;
                MethodReference? invokeMethod = null;
                Instruction? invokeCallInstruction = null;
                Instruction? firstSetLocal = null;

                foreach (var instruction in method.Body.Instructions) {
                    if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) {
                        var calleeRef = (MethodReference)instruction.Operand!;

                        if (calleeRef.Name.StartsWith("mfwh_")) {
                            mfwhMethod = calleeRef;
                            mfwhCallInstruction = instruction;
                        }
                    }
                }

                if (mfwhMethod is null) {
                    continue;
                }

                // Adjust parameters
                foreach (var instruction in method.Body.Instructions) {
                    if (instruction.OpCode != OpCodes.Newobj) {
                        continue;
                    }
                    var delegateDef = ((MethodReference)instruction.Operand).DeclaringType.Resolve();
                    if (!delegateDef.IsDelegate()) {
                        continue;
                    }
                    var beginInvoke = delegateDef.Method("BeginInvoke");
                    var invoke = delegateDef.Method("Invoke");
                    for (int i = 0; i < method.Parameters.Count; i++) {
                        beginInvoke.Parameters[i] = method.Parameters[i].Clone();
                        invoke.Parameters[i] = method.Parameters[i].Clone();
                    }
                }

                foreach (var instruction in method.Body.Instructions) {
                    if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) {
                        var calleeRef = (MethodReference)instruction.Operand!;

                        if (calleeRef.Name == mfwhMethod.Name.Replace("mfwh_", "Invoke")) {
                            invokeMethod = calleeRef;
                            invokeCallInstruction = instruction;
                        }
                    }
                }

                foreach (var instruction in method.Body.Instructions.Reverse()) {
                    if (instruction.OpCode == OpCodes.Stloc) {
                        firstSetLocal = instruction;
                        break;
                    }
                }

                if (modifiedTileParameters.TryGetValue(mfwhMethod.GetIdentifier(), out var indexes)) {
                    modifiedTileParameters.TryAdd(method.GetIdentifier(), indexes);

                    var invokeCallPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, invokeCallInstruction!, jumpTargets);
                    if (invokeCallPaths.Length > 1) {
                        throw new Exception($"Unexpected IL structure: multiple calls to EventHooks.Invoke{method.Name} in the method");
                    }
                    var mfwhCallPaths = MonoModCommon.Stack.AnalyzeParametersSources(method, mfwhCallInstruction!, jumpTargets);
                    if (mfwhCallPaths.Length > 1) {
                        throw new Exception($"Unexpected IL structure: multiple calls to the original implementation mfwh_{method.Name} in the method");
                    }

                    var invokeCallPath = invokeCallPaths[0];
                    var mfwhCallPath = mfwhCallPaths[0];

                    List<Instruction> refParameterAssignments = new();

                    foreach (var index in indexes) {
                        var paramIndexExculdeThis = index;
                        if (method.HasThis) {
                            paramIndexExculdeThis--;
                        }

                        // Exclude the 'this' parameter to make index only indicate the parameters in method.Parameters
                        if (paramIndexExculdeThis == -1) {
                            continue;
                        }

                        // The InvokeXXX is a static function, the required parameters for the call are as follows:
                        // 0. The 'this' object of the current function, or null if the current function is static
                        // 1. The original method logic delegate of the current function, which is mfwh_XXX
                        // 2 and more... The parameters of the current function
                        // Therefore, the mapping relationship from mfwh_XXX input parameter index to InvokeXXX input parameter index is +2
                        paramIndexExculdeThis += 2;

                        var loadParam = invokeCallPath.ParametersSources[paramIndexExculdeThis].Instructions.Single();

                        if (!MonoModCommon.IL.TryGetReferencedParameter(method, loadParam, out var parameter)) {
                            throw new NotSupportedException($"Cannot get the {index + 1}th parameter of Invoke{method.Name}");
                        }

                        var iLProcessor = method.Body.GetILProcessor();

                        // Since we expect to modify the parameters of the current method to match the ref parameters of mfwh_XXX,
                        // we need to extract the value of the ref parameter of the current method when calling the InvokeXXX input parameters
                        iLProcessor.InsertAfter(loadParam, Instruction.Create(OpCodes.Ldobj, tileTypeDef));

                        // Next, we need to extract the mapping relationship between the parameters and event fields from the input parameters of mfwh_XXX,
                        // so we need to convert paramIndex from InvokeXXX to the index of the input parameters of mfwh_XXX
                        paramIndexExculdeThis -= 2;

                        var loadField = mfwhCallPath.ParametersSources[paramIndexExculdeThis].Instructions.Last();

                        if (loadField.OpCode != OpCodes.Ldfld && loadField.OpCode != OpCodes.Ldflda) {
                            throw new NotSupportedException($"The {index + 1}th parameter of mfwh_{method.Name} is not a field");
                        }

                        var field = (FieldReference)loadField.Operand!;

                        loadParam = MonoModCommon.IL.BuildParameterLoad(method, method.Body, method.Parameters[paramIndexExculdeThis]);

                        // Ensure that the ref parameter can get the updated value after the InvokeXXX call
                        iLProcessor.InsertAfter(firstSetLocal!, [
                            loadParam,
                            Instruction.Create(OpCodes.Ldloc_0),
                            Instruction.Create(OpCodes.Ldfld, field),
                            Instruction.Create(OpCodes.Stobj, tileTypeDef),
                        ]);

                        loadField.Previous.OpCode = OpCodes.Nop;
                        loadField.Previous.Operand = null;
                        loadField.OpCode = loadParam.OpCode;
                        loadField.Operand = loadParam.Operand;
                    }
                }
            }
        }
    }

    private void Analyze_ModifiedTileParameter(ModFwModder modder, out Dictionary<string, HashSet<int>> modifiedTileParameter) {
        Console.WriteLine($"Preparing to analyze non-readonly methods");

        modifiedTileParameter = new();

        // Find non-readonly methods that modify fields

        foreach (var method in tileTypeDef.Methods.Where(m => !m.IsStatic && !m.IsConstructor)) {
            foreach (var instr in method.Body.Instructions.Where(instr => instr.OpCode == OpCodes.Stfld)) {
                var field = (FieldReference)instr.Operand!;
                if (field.DeclaringType.FullName == tileTypeDef.FullName) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instr, MonoModCommon.Stack.BuildJumpSitesMap(method))) {
                        if (MonoModCommon.IL.TryGetReferencedParameter(method, path.ParametersSources[0].Instructions.Last(), out var parameter) && method.Body.ThisParameter == parameter) {
                            modifiedTileParameter.Add(method.GetIdentifier(), [0]);
                            goto nextMethod;
                        }
                    }
                }
            }
        nextMethod:;
        }

        // Find non-readonly methods that call non-readonly methods
        Analyze_NonReadOnlyParameter([tileTypeDef], ref modifiedTileParameter);

        // Copy this non-readonly method from TileData to the original ITile
        foreach (var method in tileTypeOldDef.Methods) {
            if (modifiedTileParameter.TryGetValue(method.GetIdentifier(true, tileNameMap), out var byRefParamIndexesInculdeThis)) {
                modifiedTileParameter.Add(method.GetIdentifier(), byRefParamIndexesInculdeThis);
            }
        }
        foreach (var set_method in tileTypeOldDef.Methods.Where(m => m.Name.StartsWith("set_"))) {
            modifiedTileParameter.Add(set_method.GetIdentifier(), [0]);
        }

        Console.WriteLine($"Initially found {modifiedTileParameter.Count} non-readonly methods:");
        foreach (var method in modifiedTileParameter.Keys) {
            Console.WriteLine(" " + method);
        }
        Analyze_NonReadOnlyParameter(modder.Module.GetAllTypes(), ref modifiedTileParameter);
        Console.WriteLine($"Found {modifiedTileParameter.Count} methods that are not readonly");
    }

    private void Replace_TileCollection(ModFwModder modder) {
        foreach (var type in modder.Module.GetAllTypes()) {
            foreach (var method in type.Methods) {
                if (!method.HasBody) {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions) {
                    if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) {
                        var calleeRef = (MethodReference)instruction.Operand!;
                        if (calleeRef.Name == "get_Item") {
                            if (calleeRef.DeclaringType.FullName == tileCollectionDefOld.FullName) {
                                instruction.OpCode = OpCodes.Callvirt;
                                instruction.Operand = tileCollection_getItemMDef;
                            }
                        }
                    }
                }
            }
        }
    }

    private void Replace_GenericParamAndArgs(ModFwModder modder) {
        foreach (var type in modder.Module.GetAllTypes()) {
            if (type.HasGenericParameters) {
                foreach (var parameter in type.GenericParameters) {
                    foreach (var c in parameter.Constraints) {
                        if (c.ConstraintType.FullName == tileTypeOldDef.FullName) {
                            c.ConstraintType = tileTypeDef.MakeByReferenceType();
                        }
                    }
                }
            }
        }
    }

    private void Analyze_NonReadOnlyParameter(IEnumerable<TypeDefinition> types, ref Dictionary<string, HashSet<int>> database) {
        bool anyModify = false;
        do {
            anyModify = false;
            foreach (var type in types) {
                foreach (var method in type.Methods) {

                    if (!method.Parameters.Any(p => IsTileType(p.ParameterType)) && !(method.HasThis && !method.IsConstructor && IsTileType(method.DeclaringType))) {
                        continue;
                    }

                    for (int i = 0; i < method.Parameters.Count; i++) {
                        if (method.Parameters[i].ParameterType is ByReferenceType byReferenceType && IsTileType(byReferenceType.ElementType)) {
                            var index = i + (method.HasThis ? 1 : 0);
                            if (!database.TryGetValue(method.GetIdentifier(), out var indexes)) {
                                anyModify = true;
                                database.Add(method.GetIdentifier(), indexes = [index]);
                            }
                            else {
                                if (indexes.Add(index)) {
                                    anyModify = true;
                                }
                            }
                        }
                    }

                    if (!method.HasBody) {
                        continue;
                    }

                    bool skip = false;
                    foreach (var instruction in method.Body.Instructions.ToArray()) {
                        if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) {
                            var calleeRef = (MethodReference)instruction.Operand!;

                            if (!calleeRef.Name.StartsWith("mfwh_")) {
                                continue;
                            }

                            if (database.TryGetValue(calleeRef.GetIdentifier(), out var indexes)) {
                                if (database.TryAdd(method.GetIdentifier(), indexes)) {
                                    anyModify = true;
                                }
                                skip = true;
                            }
                        }
                    }

                    if (skip) {
                        continue;
                    }

                    var jumpTargets = MonoModCommon.Stack.BuildJumpSitesMap(method);

                    Queue<MonoModCommon.Stack.StackTopTypePath> works = new();
                    HashSet<MonoModCommon.Stack.StackTopTypePath> visited = new();

                    foreach (var instruction in method.Body.Instructions) {
                        switch (instruction.OpCode.Code) {
                            case Code.Newobj:
                            case Code.Callvirt:
                            case Code.Call: {
                                    var calleeRef = (MethodReference)instruction.Operand!;
                                    if (database.TryGetValue(calleeRef.GetIdentifier(), out var theseParametersWillBeEdit)) {
                                        foreach (var methodCallPath in MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets)) {
                                            foreach (var index in theseParametersWillBeEdit) {

                                                var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                                    method,
                                                    methodCallPath.ParametersSources[index].Instructions.Last(),
                                                    jumpTargets);

                                                foreach (var path in paths) {
                                                    works.Enqueue(path);
                                                }
                                            }
                                        }
                                    }
                                    else if (instruction.OpCode == OpCodes.Newobj) {
                                        var ctor = (MethodReference)instruction.Operand!;
                                        var declaringType = ctor.DeclaringType.Resolve();
                                        if (declaringType.BaseType.FullName != "System.MulticastDelegate") {
                                            break;
                                        }

                                        var paths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpTargets)
                                            .SelectMany(methodCallPath => MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, methodCallPath.ParametersSources[1].Instructions.Last(), jumpTargets));

                                        foreach (var path in paths) {
                                            if (path.Instructions.Length == 1 && path.Instructions[0].OpCode == OpCodes.Ldftn) {
                                                var ldftn = ((MethodReference)path.Instructions[0].Operand).Resolve();

                                                if (database.TryGetValue(ldftn.GetIdentifier(), out var indexes)) {
                                                    HashSet<int> newIndexes;
                                                    if (ldftn.HasThis && !ldftn.IsConstructor) {
                                                        newIndexes = new(indexes);
                                                        newIndexes.Remove(0);
                                                    }
                                                    else {
                                                        newIndexes = new(indexes.Select(index => index + 1));
                                                    }

                                                    var delegateInvoke = declaringType.Method("Invoke");
                                                    if (!database.TryAdd(delegateInvoke.GetIdentifier(), newIndexes)) {
                                                        database[delegateInvoke.GetIdentifier()] = indexes;
                                                    }
                                                    delegateInvoke = declaringType.Method("BeginInvoke");
                                                    if (!database.TryAdd(delegateInvoke.GetIdentifier(), newIndexes)) {
                                                        database[delegateInvoke.GetIdentifier()] = indexes;
                                                    }
                                                }

                                                break;
                                            }
                                        }
                                    }
                                    break;
                                }
                            case Code.Stfld: {
                                    var field = (FieldReference)instruction.Operand!;
                                    if (IsTileType(field.DeclaringType)) {
                                        foreach (var fieldPath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                                            var paths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                                                method,
                                                fieldPath.ParametersSources[0].Instructions.Last(),
                                                jumpTargets);
                                            foreach (var path in paths) {
                                                works.Enqueue(path);
                                            }
                                        }
                                    }
                                    break;
                                }
                        }
                    }

                    while (works.Count > 0) {
                        var path = works.Dequeue();
                        if (visited.Add(path)) {

                            var thisInstruction = path.Instructions.First();
                            switch (thisInstruction.OpCode.Code) {
                                case Code.Ldarg:
                                case Code.Ldarg_S:
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                    // Since the 'this' parameter of a constructor does not need to be pushed onto the stack explicitly by the caller,
                                    // it will not modify the 'this' variable provided by the caller like other instance methods.
                                    // Therefore, we need to handle this case separately and skip it manually
                                    if (thisInstruction.OpCode == OpCodes.Ldarg_0 && method.IsConstructor) {
                                        break;
                                    }

                                    var index = thisInstruction.OpCode.Code switch {
                                        Code.Ldarg_0 => 0,
                                        Code.Ldarg_1 => 1,
                                        Code.Ldarg_2 => 2,
                                        Code.Ldarg_3 => 3,
                                        _ => ((ParameterDefinition)thisInstruction.Operand).Index
                                        + ((method.HasThis) ? 1 : 0)
                                    };

                                    if (!database.TryGetValue(method.GetIdentifier(), out var indexes)) {
                                        anyModify = true;
                                        database.Add(method.GetIdentifier(), indexes = [index]);
                                    }
                                    else {
                                        if (indexes.Add(index)) {
                                            anyModify = true;
                                        }
                                    }
                                    break;

                                case Code.Ldloc:
                                case Code.Ldloc_S:
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:

                                    var variable = MonoModCommon.IL.GetReferencedVariable(method, thisInstruction);

                                    foreach (var instruction in method.Body.Instructions.ToArray()) {
                                        if (instruction.OpCode.Code is
                                            Code.Stloc_0 or
                                            Code.Stloc_1 or
                                            Code.Stloc_2 or
                                            Code.Stloc_3 or
                                            Code.Stloc_S or
                                            Code.Stloc) {

                                            var checkVariable = MonoModCommon.IL.GetReferencedVariable(method, instruction);

                                            if (checkVariable == variable) {
                                                foreach (var sourcePath in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpTargets)) {
                                                    foreach (var variablePath in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, sourcePath.ParametersSources[0].Instructions.Last())) {
                                                        if (!visited.Contains(variablePath)) {
                                                            works.Enqueue(variablePath);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    variable.VariableType = tileTypeDef.MakeByReferenceType();

                                    break;
                            }
                        }
                    }
                }
            }
        }
        while (anyModify);

        foreach (var type in types) {
            foreach (var method in type.Methods) {
                if (database.TryGetValue(method.GetIdentifier(), out var byRefParamIndexesInculdeThis)) {
                    HashSet<int> byRefParamIndexesExculdeThis = [];
                    foreach (var index in byRefParamIndexesInculdeThis) {
                        var paramIndex = index;
                        if (method.HasThis) {
                            paramIndex -= 1;
                        }

                        if (paramIndex != -1) {
                            byRefParamIndexesExculdeThis.Add(paramIndex);
                            method.Parameters[paramIndex].ParameterType = tileTypeDef.MakeByReferenceType();
                        }
                    }

                    // Intermediate step of transform
                    var newerMethodKey = method.GetIdentifier(true, [], byRefParamIndexesExculdeThis);
                    if (!database.TryAdd(newerMethodKey, byRefParamIndexesInculdeThis)) {
                        database[newerMethodKey] = byRefParamIndexesInculdeThis;
                    }

                    // Final step of transform
                    newerMethodKey = method.GetIdentifier(true, tileNameMap, byRefParamIndexesExculdeThis);
                    if (!database.TryAdd(newerMethodKey, byRefParamIndexesInculdeThis)) {
                        database[newerMethodKey] = byRefParamIndexesInculdeThis;
                    }
                }
            }
        }
    }
}

namespace Terraria
{
    public abstract class TileCollection : IDisposable {
        public abstract int Width { get; }
        public abstract int Height { get; }
        public abstract ref TileData this[int x, int y] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract RefTileData GetRefTile(int x, int y);
        public static CreateTileCollectionDele? OnCreate;
        public static TileCollection Create(int width, int height, string callingMethod) {
            return OnCreate?.Invoke(width, height, callingMethod) ?? new DefaultTileCollection(width, height);
        }
        public abstract void Dispose();

        public class DefaultTileCollection(int width, int height) : TileCollection {
            [StructLayout(LayoutKind.Sequential, Pack = 0, Size = 4)]
            readonly unsafe struct Point(int x, int y) {
                public readonly ushort X = (ushort)x;
                public readonly ushort Y = (ushort)y;
                public static nint Serialize(Point p) => *(nint*)&p;
                public static Point Deserialize(nint p) => *(Point*)&p;
            }
            static unsafe readonly delegate*<object?, nint, ref TileData> cached_RefTileData_GetTileRef = &RefTileData_GetTileRef;
            static ref TileData RefTileData_GetTileRef(object? managedData, nint unmanagedData) {
                var pos = Point.Deserialize(unmanagedData);
                return ref ((TileData[,])managedData!)[pos.X, pos.Y];
            }

            readonly int
                width = width, 
                height = height;

            readonly TileData[,] 
                data = new TileData[width, height];
            public sealed override ref TileData this[int x, int y] => ref data[x, y];
            public unsafe sealed override RefTileData GetRefTile(int x, int y) => new(data, Point.Serialize(new(x, y)), cached_RefTileData_GetTileRef);
            public sealed override int Width => width;
            public sealed override int Height => height;

            public sealed override void Dispose() { }
        }
        public unsafe class UnsafeTileCollection(int width, int height) : TileCollection
        {
            static unsafe readonly delegate*<object?, nint, ref TileData> cached_RefTileData_GetTileRef = &RefTileData_GetTileRef;
            static ref TileData RefTileData_GetTileRef(object? _, nint unmanagedData) {
                return ref *(TileData*)unmanagedData;
            }
            readonly int 
                width = width,
                height = height;

            TileData* data = (TileData*)NativeMemory.AllocZeroed((nuint)(sizeof(TileData) * width * height));

            public sealed override ref TileData this[int x, int y] => ref data[x + y * width];
            public sealed override RefTileData GetRefTile(int x, int y) => new(null, (nint)Unsafe.Add<TileData>(data, x + y * width), &RefTileData_GetTileRef);
            public sealed override int Width => width;
            public sealed override int Height => height;

            public sealed override void Dispose() {
                Dispose(true);
                GC.SuppressFinalize(this); 
            }

            protected virtual void Dispose(bool disposing) {
                if (data is null) return; 

                NativeMemory.Free(data);
                data = null; 
            }

            ~UnsafeTileCollection() => Dispose(false);
        }
    }
    public delegate TileCollection CreateTileCollectionDele(int width, int height, string callingMethod);
    public readonly unsafe struct RefTileData(object? managedData, nint unmanagedData, delegate*<object?, nint, ref TileData> getRefFunc) : IEquatable<RefTileData>
    {
        static ref TileData RefTileData_GetTempRef(object? managedData, nint _) {
            return ref ((Ref<TileData>)managedData!).Value;
        }
        static readonly delegate*<object?, nint, ref TileData> cachedGetRefFunc = &RefTileData_GetTempRef;
        public static RefTileData Temporary => new(new Ref<TileData>(), default, cachedGetRefFunc);

        readonly object? managedData = managedData;
        readonly nint unmanagedData = unmanagedData;
        readonly delegate*<object?, nint, ref TileData> getRefFunc = getRefFunc;
        public readonly ref TileData Data {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref getRefFunc(managedData, unmanagedData);
        }
        public readonly bool Equals(RefTileData other) => managedData == other.managedData && unmanagedData == other.unmanagedData;
        public readonly override bool Equals(object? obj) => obj is RefTileData refTile && Equals(refTile);
        public static bool operator ==(RefTileData left, RefTileData right) => left.Equals(right);
        public static bool operator !=(RefTileData left, RefTileData right) => !(left == right);
        public readonly override int GetHashCode() => HashCode.Combine(managedData, unmanagedData);
    }
    public struct TileData : IEquatable<TileData>
    {
        public TileData() { }
        public TileData(TileData from) {
            if (from.IsNull) {
                type = 0;
                wall = 0;
                liquid = 0;
                sTileHeader = 0;
                bTileHeader = 0;
                bTileHeader2 = 0;
                bTileHeader3 = 0;
                frameX = 0;
                frameY = 0;
            }
            type = from.type;
            wall = from.wall;
            sTileHeader = from.sTileHeader;
            frameX = from.frameX;
            frameY = from.frameY;
            liquid = from.liquid;
            bTileHeader = from.bTileHeader;
            bTileHeader2 = from.bTileHeader2;
            bTileHeader3 = from.bTileHeader3;
        }
        public static TileData New() => new();
        public static TileData New(TileData from) => new(from);

        public static readonly TileData NULL = new() { type = ushort.MaxValue };
        public static readonly TileData EMPTY = default;


        static readonly ThreadLocal<Ref<TileData>> tmpNullRef = new();
        static readonly ThreadLocal<Ref<TileData>> tmpEmptyRef = new();

        public static ref TileData NULLREF {
            get {
                tmpNullRef.Value = new Ref<TileData>(NULL);
                return ref tmpNullRef.Value.Value;
            }
        }
        public static ref TileData EMPTYREF {
            get {
                tmpEmptyRef.Value = new Ref<TileData>(default);
                return ref tmpEmptyRef.Value.Value;
            }
        }
        public static ref TileData GetEMPTYREF() => ref EMPTYREF;
        public static ref TileData GetEMPTYREF(TileData existing) => ref EMPTYREF;
        public static ref TileData GetEMPTYREF(ref TileData existing) => ref EMPTYREF;

        public readonly bool IsNull => type == ushort.MaxValue;
        public readonly bool IsNotNull => type != ushort.MaxValue;

        public ushort type;
        public ushort wall;
        public ushort sTileHeader;
        public short frameX;
        public short frameY;
        public byte liquid;
        public byte bTileHeader;
        public byte bTileHeader2;
        public byte bTileHeader3;

        public readonly override bool Equals(object? obj) => obj is TileData other && Equals(other);
        public readonly bool Equals(TileData other) {
            return type == other.type &&
                wall == other.wall &&
                sTileHeader == other.sTileHeader &&
                frameX == other.frameX &&
                frameY == other.frameY &&
                liquid == other.liquid &&
                bTileHeader == other.bTileHeader &&
                bTileHeader2 == other.bTileHeader2 &&
                bTileHeader3 == other.bTileHeader3;
        }
        public readonly override int GetHashCode() {
            return HashCode.Combine(
                type << 16 | wall,
                sTileHeader,
                (ushort)frameX << 16 | (ushort)frameY,
                liquid << 16 | bTileHeader << 8 | bTileHeader2 << 4 | bTileHeader3);
        }
        public static bool operator ==(TileData a, TileData b) => a.Equals(b);
        public static bool operator !=(TileData a, TileData b) => !a.Equals(b);
        private static readonly object _lock = new();
        public object GetLock() => _lock;

        #region Implement Method
        public readonly int collisionType {
            get {
                if (!active()) {
                    return 0;
                }

                if (halfBrick()) {
                    return 2;
                }

                if (slope() > 0) {
                    return 2 + slope();
                }

                if (Main.tileSolid[type] && !Main.tileSolidTop[type]) {
                    return 1;
                }

                return -1;
            }
        }

        public readonly object Clone() {
            return MemberwiseClone();
        }

        public void ClearEverything() {
            type = 0;
            wall = 0;
            liquid = 0;
            sTileHeader = 0;
            bTileHeader = 0;
            bTileHeader2 = 0;
            bTileHeader3 = 0;
            frameX = 0;
            frameY = 0;
        }
        public void ClearTile() {
            slope(0);
            halfBrick(halfBrick: false);
            active(active: false);
            inActive(inActive: false);
        }

        public void CopyFrom(TileData from) {
            type = from.type;
            wall = from.wall;
            liquid = from.liquid;
            sTileHeader = from.sTileHeader;
            bTileHeader = from.bTileHeader;
            bTileHeader2 = from.bTileHeader2;
            bTileHeader3 = from.bTileHeader3;
            frameX = from.frameX;
            frameY = from.frameY;
        }

        public readonly bool isTheSameAs(TileData compTile) {
            if (compTile.IsNull) {
                return false;
            }

            if (sTileHeader != compTile.sTileHeader) {
                return false;
            }

            if (active()) {
                if (type != compTile.type) {
                    return false;
                }

                if (Main.tileFrameImportant[type] && (frameX != compTile.frameX || frameY != compTile.frameY)) {
                    return false;
                }
            }

            if (wall != compTile.wall || liquid != compTile.liquid) {
                return false;
            }

            if (compTile.liquid == 0) {
                if (wallColor() != compTile.wallColor()) {
                    return false;
                }

                if (wire4() != compTile.wire4()) {
                    return false;
                }
            }
            else if (bTileHeader != compTile.bTileHeader) {
                return false;
            }

            if (invisibleBlock() != compTile.invisibleBlock() || invisibleWall() != compTile.invisibleWall() || fullbrightBlock() != compTile.fullbrightBlock() || fullbrightWall() != compTile.fullbrightWall()) {
                return false;
            }

            return true;
        }

        public readonly int blockType() {
            if (halfBrick()) {
                return 1;
            }

            int num = slope();
            if (num > 0) {
                num++;
            }

            return num;
        }

        public void liquidType(int liquidType) {
            switch (liquidType) {
                case 0:
                    bTileHeader &= 159;
                    break;
                case 1:
                    lava(lava: true);
                    break;
                case 2:
                    honey(honey: true);
                    break;
                case 3:
                    shimmer(shimmer: true);
                    break;
            }
        }

        public readonly byte liquidType() {
            return (byte)((bTileHeader & 0x60) >> 5);
        }

        public readonly bool nactive() {
            if ((sTileHeader & 0x60) == 32) {
                return true;
            }

            return false;
        }

        public void ResetToType(ushort _type) {
            liquid = 0;
            sTileHeader = 32;
            bTileHeader = 0;
            bTileHeader2 = 0;
            bTileHeader3 = 0;
            frameX = 0;
            frameY = 0;
            this.type = _type;
        }

        public void ClearMetadata() {
            liquid = 0;
            sTileHeader = 0;
            bTileHeader = 0;
            bTileHeader2 = 0;
            bTileHeader3 = 0;
            frameX = 0;
            frameY = 0;
        }

        public readonly Color actColor(Color oldColor) {
            if (!inActive()) {
                return oldColor;
            }

            double num = 0.4;
            return new Color((byte)(num * (double)(int)oldColor.R), (byte)(num * (double)(int)oldColor.G), (byte)(num * (double)(int)oldColor.B), oldColor.A);
        }

        public readonly void actColor(ref Vector3 oldColor) {
            if (inActive()) {
                oldColor *= 0.4f;
            }
        }

        public readonly bool topSlope() {
            byte b = slope();
            if (b != 1) {
                return b == 2;
            }

            return true;
        }

        public readonly bool bottomSlope() {
            byte b = slope();
            if (b != 3) {
                return b == 4;
            }

            return true;
        }

        public readonly bool leftSlope() {
            byte b = slope();
            if (b != 2) {
                return b == 4;
            }

            return true;
        }

        public readonly bool rightSlope() {
            byte b = slope();
            if (b != 1) {
                return b == 3;
            }

            return true;
        }

        public readonly bool HasSameSlope(TileData tile) {
            return (sTileHeader & 0x7400) == (tile.sTileHeader & 0x7400);
        }

        public readonly byte wallColor() {
            return (byte)(bTileHeader & 0x1Fu);
        }

        public void wallColor(byte wallColor) {
            bTileHeader = (byte)((bTileHeader & 0xE0u) | wallColor);
        }

        public readonly bool lava() {
            return (bTileHeader & 0x60) == 32;
        }

        public void lava(bool lava) {
            if (lava) {
                bTileHeader = (byte)((bTileHeader & 0x9Fu) | 0x20u);
            }
            else {
                bTileHeader &= 223;
            }
        }

        public readonly bool honey() {
            return (bTileHeader & 0x60) == 64;
        }

        public void honey(bool honey) {
            if (honey) {
                bTileHeader = (byte)((bTileHeader & 0x9Fu) | 0x40u);
            }
            else {
                bTileHeader &= 191;
            }
        }

        public readonly bool shimmer() {
            return (bTileHeader & 0x60) == 96;
        }

        public void shimmer(bool shimmer) {
            if (shimmer) {
                bTileHeader = (byte)((bTileHeader & 0x9Fu) | 0x60u);
            }
            else {
                bTileHeader &= 159;
            }
        }

        public readonly bool wire4() {
            return (bTileHeader & 0x80) == 128;
        }

        public void wire4(bool wire4) {
            if (wire4) {
                bTileHeader |= 128;
            }
            else {
                bTileHeader &= 127;
            }
        }

        public readonly int wallFrameX() {
            return (bTileHeader2 & 0xF) * 36;
        }

        public void wallFrameX(int wallFrameX) {
            bTileHeader2 = (byte)((bTileHeader2 & 0xF0u) | ((uint)(wallFrameX / 36) & 0xFu));
        }

        public readonly byte frameNumber() {
            return (byte)((bTileHeader2 & 0x30) >> 4);
        }

        public void frameNumber(byte frameNumber) {
            bTileHeader2 = (byte)((bTileHeader2 & 0xCFu) | (uint)((frameNumber & 3) << 4));
        }

        public readonly byte wallFrameNumber() {
            return (byte)((bTileHeader2 & 0xC0) >> 6);
        }

        public void wallFrameNumber(byte wallFrameNumber) {
            bTileHeader2 = (byte)((bTileHeader2 & 0x3Fu) | (uint)((wallFrameNumber & 3) << 6));
        }

        public readonly int wallFrameY() {
            return (bTileHeader3 & 7) * 36;
        }

        public void wallFrameY(int wallFrameY) {
            bTileHeader3 = (byte)((bTileHeader3 & 0xF8u) | ((uint)(wallFrameY / 36) & 7u));
        }

        public readonly bool checkingLiquid() {
            return (bTileHeader3 & 8) == 8;
        }

        public void checkingLiquid(bool checkingLiquid) {
            if (checkingLiquid) {
                bTileHeader3 |= 8;
            }
            else {
                bTileHeader3 &= 247;
            }
        }

        public readonly bool skipLiquid() {
            return (bTileHeader3 & 0x10) == 16;
        }

        public void skipLiquid(bool skipLiquid) {
            if (skipLiquid) {
                bTileHeader3 |= 16;
            }
            else {
                bTileHeader3 &= 239;
            }
        }

        public readonly bool invisibleBlock() {
            return (bTileHeader3 & 0x20) == 32;
        }

        public void invisibleBlock(bool invisibleBlock) {
            if (invisibleBlock) {
                bTileHeader3 |= 32;
            }
            else {
                bTileHeader3 = (byte)(bTileHeader3 & 0xFFFFFFDFu);
            }
        }

        public readonly bool invisibleWall() {
            return (bTileHeader3 & 0x40) == 64;
        }

        public void invisibleWall(bool invisibleWall) {
            if (invisibleWall) {
                bTileHeader3 |= 64;
            }
            else {
                bTileHeader3 = (byte)(bTileHeader3 & 0xFFFFFFBFu);
            }
        }

        public readonly bool fullbrightBlock() {
            return (bTileHeader3 & 0x80) == 128;
        }

        public void fullbrightBlock(bool fullbrightBlock) {
            if (fullbrightBlock) {
                bTileHeader3 |= 128;
            }
            else {
                bTileHeader3 = (byte)(bTileHeader3 & 0xFFFFFF7Fu);
            }
        }

        public readonly byte color() {
            return (byte)(sTileHeader & 0x1Fu);
        }

        public void color(byte color) {
            sTileHeader = (ushort)((sTileHeader & 0xFFE0u) | color);
        }

        public readonly bool active() {
            return (sTileHeader & 0x20) == 32;
        }

        public void active(bool active) {
            if (active) {
                sTileHeader |= 32;
            }
            else {
                sTileHeader &= 65503;
            }
        }

        public readonly bool inActive() {
            return (sTileHeader & 0x40) == 64;
        }

        public void inActive(bool inActive) {
            if (inActive) {
                sTileHeader |= 64;
            }
            else {
                sTileHeader &= 65471;
            }
        }

        public readonly bool wire() {
            return (sTileHeader & 0x80) == 128;
        }

        public void wire(bool wire) {
            if (wire) {
                sTileHeader |= 128;
            }
            else {
                sTileHeader &= 65407;
            }
        }

        public readonly bool wire2() {
            return (sTileHeader & 0x100) == 256;
        }

        public void wire2(bool wire2) {
            if (wire2) {
                sTileHeader |= 256;
            }
            else {
                sTileHeader &= 65279;
            }
        }

        public readonly bool wire3() {
            return (sTileHeader & 0x200) == 512;
        }

        public void wire3(bool wire3) {
            if (wire3) {
                sTileHeader |= 512;
            }
            else {
                sTileHeader &= 65023;
            }
        }

        public readonly bool halfBrick() {
            return (sTileHeader & 0x400) == 1024;
        }

        public void halfBrick(bool halfBrick) {
            if (halfBrick) {
                sTileHeader |= 1024;
            }
            else {
                sTileHeader &= 64511;
            }
        }

        public readonly bool actuator() {
            return (sTileHeader & 0x800) == 2048;
        }

        public void actuator(bool actuator) {
            if (actuator) {
                sTileHeader |= 2048;
            }
            else {
                sTileHeader &= 63487;
            }
        }

        public readonly byte slope() {
            return (byte)((sTileHeader & 0x7000) >> 12);
        }

        public void slope(byte slope) {
            sTileHeader = (ushort)((sTileHeader & 0x8FFFu) | (uint)((slope & 7) << 12));
        }

        public readonly bool fullbrightWall() {
            return (sTileHeader & 0x8000) == 32768;
        }

        public void fullbrightWall(bool fullbrightWall) {
            if (fullbrightWall) {
                sTileHeader |= 32768;
            }
            else {
                sTileHeader = (ushort)(sTileHeader & 0xFFFF7FFFu);
            }
        }

        public void Clear(TileDataType types) {
            if ((types & TileDataType.Tile) != 0) {
                type = 0;
                active(active: false);
                frameX = 0;
                frameY = 0;
            }

            if ((types & TileDataType.Wall) != 0) {
                wall = 0;
                wallFrameX(0);
                wallFrameY(0);
            }

            if ((types & TileDataType.TilePaint) != 0) {
                ClearBlockPaintAndCoating();
            }

            if ((types & TileDataType.WallPaint) != 0) {
                ClearWallPaintAndCoating();
            }

            if ((types & TileDataType.Liquid) != 0) {
                liquid = 0;
                liquidType(0);
                checkingLiquid(checkingLiquid: false);
            }

            if ((types & TileDataType.Slope) != 0) {
                slope(0);
                halfBrick(halfBrick: false);
            }

            if ((types & TileDataType.Wiring) != 0) {
                wire(wire: false);
                wire2(wire2: false);
                wire3(wire3: false);
                wire4(wire4: false);
            }

            if ((types & TileDataType.Actuator) != 0) {
                actuator(actuator: false);
                inActive(inActive: false);
            }
        }

        public void CopyPaintAndCoating(TileData other) {
            color(other.color());
            invisibleBlock(other.invisibleBlock());
            fullbrightBlock(other.fullbrightBlock());
        }

        public readonly TileColorCache BlockColorAndCoating() {
            TileColorCache result = default(TileColorCache);
            result.Color = color();
            result.FullBright = fullbrightBlock();
            result.Invisible = invisibleBlock();
            return result;
        }

        public readonly TileColorCache WallColorAndCoating() {
            TileColorCache result = default(TileColorCache);
            result.Color = wallColor();
            result.FullBright = fullbrightWall();
            result.Invisible = invisibleWall();
            return result;
        }

        public void UseBlockColors(TileColorCache cache) {
            color(cache.Color);
            fullbrightBlock(cache.FullBright);
            invisibleBlock(cache.Invisible);
        }

        public void UseWallColors(TileColorCache cache) {
            wallColor(cache.Color);
            fullbrightWall(cache.FullBright);
            invisibleWall(cache.Invisible);
        }

        public void ClearBlockPaintAndCoating() {
            color(0);
            fullbrightBlock(fullbrightBlock: false);
            invisibleBlock(invisibleBlock: false);
        }

        public void ClearWallPaintAndCoating() {
            wallColor(0);
            fullbrightWall(fullbrightWall: false);
            invisibleWall(invisibleWall: false);
        }

        public readonly override string ToString() {
            return "Tile Type:" + type + " Active:" + active().ToString() + " Wall:" + wall + " Slope:" + slope() + " fX:" + frameX + " fY:" + frameY;
        }

        public void Initialise() {
            type = 0;
            wall = 0;
            liquid = 0;
            sTileHeader = 0;
            bTileHeader = 0;
            bTileHeader2 = 0;
            bTileHeader3 = 0;
            frameX = 0;
            frameY = 0;
        }
        #endregion
    }
}
