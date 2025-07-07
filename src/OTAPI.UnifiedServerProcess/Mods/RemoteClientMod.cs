#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
using ModFramework;
using System;

[Modification(ModType.PreWrite, "Rewrite RemoteClient ResetSections", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void NetplayConnectionCheck(ModFwModder modder) {
    Console.WriteLine("Rewrited Terraria.RemoteClient.ResetSections");
}

namespace Terraria
{
    public class patch_RemoteClient : RemoteClient
    {
        [MonoMod.MonoModReplace]
        public new void mfwh_ResetSections() {
            if (Main.maxSectionsX > TileSections.GetLength(0) || Main.maxSectionsY > TileSections.GetLength(1)) {
                TileSections = new bool[Main.maxSectionsX, Main.maxSectionsY];
                return;
            }
            for (int i = 0; i < Main.maxSectionsX; i++) {
                for (int j = 0; j < Main.maxSectionsY; j++) {
                    TileSections[i, j] = false;
                }
            }
        }
        [MonoMod.MonoModReplace]
        public new void mfwh_orig_ctor_RemoteClient() {
            Name = "Anonymous";
            StatusText = "";
            TileSections = new bool[2, 2];
            SpamProjectileMax = 100f;
            SpamAddBlockMax = 100f;
            SpamDeleteBlockMax = 500f;
            SpamWaterMax = 50f;
        }
    }
}
