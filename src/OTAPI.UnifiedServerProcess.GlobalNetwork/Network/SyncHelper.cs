using Microsoft.Xna.Framework;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using Terraria.ID;
using Terraria.Localization;
using Terraria;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Network
{
    public static class SyncHelper
    {
        #region Sync Server Online To Player
        public static void SyncServerOnlineToPlayer(ServerContext onlineServer, int plr) {
            onlineServer.NetMessage.TrySendData(MessageID.WorldData, plr);
            onlineServer.Main.SyncAnInvasion(plr);
            onlineServer.SendSectionsWhenJoin(plr);
            onlineServer.SendWorldEntities(plr);
            onlineServer.SendWorldInfo(plr);
        }

        static void SendSectionsWhenJoin(this ServerContext server, int whoAmI) {
            int spawnSectionXBegin = Terraria.Netplay.GetSectionX(server.Main.spawnTileX) - 2;
            int spawnSectionYBegin = Terraria.Netplay.GetSectionY(server.Main.spawnTileY) - 1;
            int spawnSectionXEnd = spawnSectionXBegin + 5;
            int spawnSectionYEnd = spawnSectionYBegin + 3;
            if (spawnSectionXBegin < 0) {
                spawnSectionXBegin = 0;
            }
            if (spawnSectionXEnd >= server.Main.maxSectionsX) {
                spawnSectionXEnd = server.Main.maxSectionsX;
            }
            if (spawnSectionYBegin < 0) {
                spawnSectionYBegin = 0;
            }
            if (spawnSectionYEnd >= server.Main.maxSectionsY) {
                spawnSectionYEnd = server.Main.maxSectionsY;
            }
            List<Point> existingPos = new((spawnSectionXEnd - spawnSectionXBegin) * (spawnSectionYEnd - spawnSectionYBegin));
            for (int x = spawnSectionXBegin; x < spawnSectionXEnd; x++) {
                for (int y = spawnSectionYBegin; y < spawnSectionYEnd; y++) {
                    server.NetMessage.SendSection(x, y, whoAmI);
                    existingPos.Add(new Point(x, y));
                }
            }
            server.PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, existingPos, out var portalSections);
            foreach (var section in portalSections) {
                server.NetMessage.SendSection(section.X, section.Y, whoAmI);
            }
        }
        static void SendWorldEntities(this ServerContext server, int whoAmI) {
            server.NetMessage.SyncConnectedPlayer(whoAmI);
            for (int i = 0; i < Terraria.Main.maxItems; i++) {
                if (server.Main.item[i].active) {
                    server.NetMessage.TrySendData(MessageID.SyncItem, whoAmI, -1, null, i);
                    server.NetMessage.TrySendData(MessageID.ItemOwner, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxNPCs; i++) {
                if (server.Main.npc[i].active) {
                    server.NetMessage.TrySendData(MessageID.SyncNPC, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                if (server.Main.projectile[i].active && (server.Main.projPet[server.Main.projectile[i].type] || server.Main.projectile[i].netImportant)) {
                    server.NetMessage.TrySendData(MessageID.SyncProjectile, whoAmI, -1, null, i);
                }
            }
        }
        static void SendWorldInfo(this ServerContext server, int whoAmI) {
            for (int i = 0; i < 290; i++) {
                server.NetMessage.TrySendData(MessageID.NPCKillCountDeathTally, whoAmI, -1, null, i);
            }
            server.NetMessage.TrySendData(57, whoAmI);
            server.NetMessage.TrySendData(MessageID.MoonlordHorror);
            server.NetMessage.TrySendData(MessageID.UpdateTowerShieldStrengths, whoAmI);
            server.NetMessage.TrySendData(MessageID.SyncCavernMonsterType, whoAmI);
            server.Main.BestiaryTracker.OnPlayerJoining(server, whoAmI);
            server.CreativePowerManager.SyncThingsToJoiningPlayer(whoAmI);
            server.Main.PylonSystem.OnPlayerJoining(server, whoAmI);
            server.NetMessage.TrySendData(MessageID.AnglerQuest, whoAmI, -1, NetworkText.FromLiteral(server.Main.player[whoAmI].name), server.Main.anglerQuest);
        }
        #endregion

        #region Sync Server Offline To Player
        public static void SyncServerOfflineToPlayer(ServerContext offlineServer, int plr) {

        }
        #endregion

        #region Sync Player Join To Others
        public static void SyncPlayerJoinToOthers(ServerContext onlineServer, int whoAmI) {
            var player = onlineServer.Main.player[whoAmI];
            onlineServer.NetMessage.TrySendData(MessageID.PlayerActive, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(MessageID.SyncPlayer, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(68, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(MessageID.PlayerLifeMana, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(42, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(MessageID.PlayerBuffs, -1, whoAmI, null, whoAmI);
            onlineServer.NetMessage.TrySendData(MessageID.SyncLoadout, -1, whoAmI, null, whoAmI, player.CurrentLoadoutIndex);
            for (int i = 0; i < 59; i++) {
                onlineServer.NetMessage.TrySendData(MessageID.SyncEquipment, -1, whoAmI, null, whoAmI, PlayerItemSlotID.Inventory0 + i, player.inventory[i].prefix);
            }
            onlineServer.TrySendingItemArray(whoAmI, player.armor, PlayerItemSlotID.Armor0);
            onlineServer.TrySendingItemArray(whoAmI, player.dye, PlayerItemSlotID.Dye0);
            onlineServer.TrySendingItemArray(whoAmI, player.miscEquips, PlayerItemSlotID.Misc0);
            onlineServer.TrySendingItemArray(whoAmI, player.miscDyes, PlayerItemSlotID.MiscDye0);
            onlineServer.TrySendingItemArray(whoAmI, player.bank.item, PlayerItemSlotID.Bank1_0);
            onlineServer.TrySendingItemArray(whoAmI, player.bank2.item, PlayerItemSlotID.Bank2_0);
            onlineServer.NetMessage.TrySendData(5, -1, whoAmI, null, whoAmI, PlayerItemSlotID.TrashItem, player.trashItem.prefix);
            onlineServer.TrySendingItemArray(whoAmI, player.bank3.item, PlayerItemSlotID.Bank3_0);
            onlineServer.TrySendingItemArray(whoAmI, player.bank4.item, PlayerItemSlotID.Bank4_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[0].Armor, PlayerItemSlotID.Loadout1_Armor_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[0].Dye, PlayerItemSlotID.Loadout1_Dye_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[1].Armor, PlayerItemSlotID.Loadout2_Armor_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[1].Dye, PlayerItemSlotID.Loadout2_Dye_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[2].Armor, PlayerItemSlotID.Loadout3_Armor_0);
            onlineServer.TrySendingItemArray(whoAmI, player.Loadouts[2].Dye, PlayerItemSlotID.Loadout3_Dye_0);

            onlineServer.NetMessage.SendData(MessageID.PlayerSpawn, whoAmI, -1, null, whoAmI, (byte)PlayerSpawnContext.SpawningIntoWorld);
            player.position = new(
                onlineServer.Main.spawnTileX * 16 + 8 - player.width / 2, 
                onlineServer.Main.spawnTileY * 16 - player.height);
            player.velocity = default;
            onlineServer.NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, whoAmI, player.position.X, player.position.Y, -1);
            onlineServer.NetMessage.greetPlayer(whoAmI);
        }
        static void TrySendingItemArray(this ServerContext onlineServer, int plr, Item[] array, int slotStartIndex) {
            for (int i = 0; i < array.Length; i++) {
                onlineServer.NetMessage.TrySendData(5, -1, plr, null, plr, slotStartIndex + i, array[i].prefix);
            }
        }
        #endregion

        #region Sync Player Leave To Others
        public static void SyncPlayerLeaveToOthers(ServerContext offlineServer, int plr) {
            offlineServer.NetMessage.SendData(MessageID.PlayerActive, -1, plr, null, plr, 0);
        }
        #endregion
    }
}
