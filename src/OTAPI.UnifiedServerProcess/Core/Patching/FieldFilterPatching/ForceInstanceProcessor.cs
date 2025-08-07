using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class ForceInstanceProcessor() : IFieldFilterArgProcessor
    {
        readonly static string[] fields = new string[] {
            // "Terraria.Main.AnnouncementBoxRange",
            "Terraria.DataStructures.TileEntity.manager",
            "Terraria.DataStructures.TileEntity.EntityCreationLock",
            "Terraria.GameContent.PressurePlateHelper.EntityCreationLock",
            "Terraria.GameContent.Creative.CreativePowerManager._initialized",
            "Terraria.Recipe.numRecipes",
            "Terraria.ID.ContentSamples.NpcBestiarySortingId",
            "Terraria.Main.autoGen",
            "Terraria.Main.AutogenSeedName",
            "Terraria.Main.AutogenProgress",
            "Terraria.NPC.defaultMaxSpawns",
            "Terraria.NPC.defaultSpawnRate",
        };
        readonly static string[] types = new string[] {
            "Terraria.ObjectData.TileObjectData",
        };
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var fieldId in fields) {
                if (source.UnmodifiedStaticFields.TryGetValue(fieldId, out var field) || source.InitialStaticFields.TryGetValue(fieldId, out field)) {
                    source.ModifiedStaticFields.TryAdd(fieldId, field);
                    source.UnmodifiedStaticFields.Remove(fieldId);
                    source.InitialStaticFields.Remove(fieldId);
                }
            }
            var typeSets = types.ToHashSet();
            foreach (var fieldKV in source.UnmodifiedStaticFields.Concat(source.InitialStaticFields).ToArray()) {
                if (typeSets.Contains(fieldKV.Value.DeclaringType.FullName)) {
                    var field = fieldKV.Value;
                    var fieldId = fieldKV.Key;
                    source.ModifiedStaticFields.TryAdd(fieldId, field);
                    source.UnmodifiedStaticFields.Remove(fieldId);
                    source.InitialStaticFields.Remove(fieldId);
                }
            }
        }
    }
}
