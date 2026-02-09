using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

[Modification(ModType.PostMerge, "Reset Sections aware array when reset world size", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void PatchSectionsAwareFields(ModFwModder modder) {

    ModuleDefinition module = modder.Module;

    MethodDefinition resetAndResize = module
        .GetType("UnifiedServerProcess.SectionsHelper")
        .GetMethod("ResetAndResize");

    MethodDefinition setWorldSize = module
        .GetType("Terraria.WorldGen")
        .GetMethod("mfwh_setWorldSize");

    Instruction ret = setWorldSize.Body.Instructions.Single(i => i.OpCode.Code is Code.Ret);

    MethodDefinition activeSections_Reset = module
        .GetType("Terraria.DataStructures.ActiveSections")
        .GetMethod("Reset");
    Process(activeSections_Reset, "LastActiveTime");
    setWorldSize.Body.GetILProcessor()
        .InsertBefore(ret, Instruction.Create(OpCodes.Call, MonoModCommon.Structure.CreateMethodReference(activeSections_Reset, activeSections_Reset)));

    MethodDefinition leashedEntityClear = module
        .GetType("Terraria.GameContent.LeashedEntity")
        .GetMethod("Clear");
    Process(leashedEntityClear, "BySection");


    void Process(MethodDefinition method, string fieldName) {
        foreach (Instruction? inst in method.Body.Instructions) {
            if (inst is not Instruction { OpCode.Code: Code.Call, Operand: MethodReference { DeclaringType.FullName: "System.Array", Name: "Clear" } }) {
                continue;
            }

            Dictionary<Instruction, List<Instruction>> jumpSites = MonoModCommon.Stack.BuildJumpSitesMap(method);

            MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paths = MonoModCommon.Stack.AnalyzeParametersSources(method, inst, jumpSites);

            if (paths is not [var path] ||
                path.ParametersSources[0].Instructions is not [var ldsfld] ||
                ldsfld is not Instruction { OpCode.Code: Code.Ldsfld, Operand: FieldReference fr } ||
                fr.Name != fieldName ||
                fr.FieldType is not ArrayType array ||
                array.Rank != 2) {
                continue;
            }

            FieldDefinition field = fr.Resolve();
            field.IsInitOnly = false;

            ldsfld.OpCode = OpCodes.Ldsflda;

            var mr = new GenericInstanceMethod(MonoModCommon.Structure.CreateMethodReference(resetAndResize, resetAndResize));
            mr.GenericArguments.Add(array.ElementType);

            inst.Operand = mr;
        }
    }
}

namespace UnifiedServerProcess
{
    public static class SectionsHelper
    {
        public static void ResetAndResize<T>(ref T[,] field, int s, int l) {
            var width = Main.maxTilesX / Main.sectionWidth + 1;
            var height = Main.maxTilesY / Main.sectionHeight + 1;
            if (field.GetLength(0) != width || field.GetLength(1) != height) {
                field = new T[width, height];
            }
            else {
                Array.Clear(field, 0, field.Length);
            }
        }
    }
}