#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Linq;
using Terraria;

[Modification(ModType.PostMerge, "Rewrite RemoteClient ResetSections", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void NetplayConnectionCheck(ModFwModder modder) {
    Console.WriteLine("Rewrited Terraria.RemoteClient.ResetSections");

    TypeDefinition remoteClientDef = modder.Module.GetType("Terraria.RemoteClient");
    MethodDefinition mfwh_orig_ResetMDef = remoteClientDef.GetMethod("mfwh_orig_Reset");
    System.Collections.Generic.Dictionary<Instruction, System.Collections.Generic.List<Instruction>> jumpSites = MonoModCommon.Stack.BuildJumpSitesMap(mfwh_orig_ResetMDef);

    Instruction[] clearArrayInst = mfwh_orig_ResetMDef.Body.Instructions.Select(inst => {
        if (inst is not Instruction { OpCode.Code: Code.Call, Operand: MethodReference { DeclaringType.FullName: "System.Array", Name: "Clear" } }) {
            return null;
        }
        MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] path = MonoModCommon.Stack.AnalyzeParametersSources(mfwh_orig_ResetMDef, inst, jumpSites);
        if (path.Length != 1) {
            return null;
        }
        if (path[0].ParametersSources[0].Instructions.Last() is not Instruction {
            OpCode.Code: Code.Ldfld,
            Operand: FieldReference { Name: nameof(RemoteClient.TileSections) or nameof(RemoteClient.TileSectionsCheckTime) }
        }) {
            return null;
        }

        return path[0].ParametersSources.SelectMany(x => x.Instructions).Append(inst);

    }).SelectMany(x => x ?? []).ToArray();

    foreach (Instruction? inst in clearArrayInst) {
        mfwh_orig_ResetMDef.Body.Instructions.Remove(inst);
    }

    var mfwh_ResetSectionsRef = new MethodReference(nameof(patch_RemoteClient.mfwh_ResetSections), modder.Module.TypeSystem.Void, remoteClientDef) {
        HasThis = true,
    };

    mfwh_orig_ResetMDef.Body.Instructions.InsertRange(0, [
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Call, mfwh_ResetSectionsRef),
    ]);
}

namespace Terraria
{
    public class patch_RemoteClient : RemoteClient
    {
        public void ResetSections() => mfwh_ResetSections();

        [MonoMod.MonoModReplace]
        public new void mfwh_ResetSections() {
            if (Main.maxSectionsX > TileSections.GetLength(0) || Main.maxSectionsY > TileSections.GetLength(1)) {
                TileSections = new bool[Main.maxSectionsX + 1, Main.maxSectionsY + 1];
                TileSectionsCheckTime = new uint[Main.maxSectionsX + 1, Main.maxSectionsY + 1];
                return;
            }
            Array.Clear(this.TileSections, 0, this.TileSections.Length);
            Array.Clear(this.TileSectionsCheckTime, 0, this.TileSectionsCheckTime.Length);
        }
        [MonoMod.MonoModReplace]
        public new void mfwh_orig_ctor_RemoteClient() {
            Name = "Anonymous";
            StatusText = "";
            TileSections = new bool[2 + 1, 2 + 1];
            TileSectionsCheckTime = new uint[2 + 1, 2 + 1];
            SpamProjectileMax = 100f;
            SpamAddBlockMax = 100f;
            SpamDeleteBlockMax = 500f;
            SpamWaterMax = 50f;
        }
    }
}
