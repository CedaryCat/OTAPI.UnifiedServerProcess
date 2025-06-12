using Microsoft.Xna.Framework;
using OTAPI.UnifiedServerProcess.GlobalNetwork.IO;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Utilities;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Servers
{
    public class ServerContext : RootContext
    {
        static ServerContext() {
            On.Terraria.NetplaySystemContext.UpdateServerInMainThread += MMHook_UpdateServerInMainThread;
            On.Terraria.NetplaySystemContext.UpdateConnectedClients += MMHook_UpdateConnectedClients;
        }

        public readonly Guid UniqueId = Guid.NewGuid();

        public bool IsRunning;
        public ServerContext(string worldName, byte[] worldFileData) : base(worldName) {
            Console = new ConsoleClientLauncher(this);

            var worldPath = Path.Combine(Terraria.Main.WorldPath, worldName);
            File.WriteAllBytes(worldPath, worldFileData);
            Main.ActiveWorldFileData = WorldFile.GetAllMetadata(worldPath, false);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = 1111;
            Netplay.UseUPNP = true;
        }

        private static void MMHook_UpdateConnectedClients(On.Terraria.NetplaySystemContext.orig_UpdateConnectedClients orig, NetplaySystemContext self) {
            orig(self);
            if (!self.HasClients && self.root is ServerContext server && !server.pendingClientJoins.IsEmpty) {
                self.HasClients = true;
            }
        }

        readonly ConcurrentQueue<ClientPackedData> pendingClientJoins = [];
        static void MMHook_UpdateServerInMainThread(On.Terraria.NetplaySystemContext.orig_UpdateServerInMainThread orig, NetplaySystemContext self) {
            if (self.root is ServerContext serverContext) {
                serverContext.ProcessClientJoins();
            }
            orig(self);
        }
        int RuningClientsCount() {
            int count = 0;
            for (int i = 0; i < Netplay.Clients.Length; i++) {
                var client = Netplay.Clients[i];
                if (client is not null && client.IsConnected()) {
                    count++;
                }
            }
            return count;
        }
        public int GetClientSpace() {
            return Main.maxNetPlayers - RuningClientsCount() - pendingClientJoins.Count;
        }
        public void ClientLeave(ClientPackedData data) {

            NetMessage.SendData(MessageID.PlayerActive, -1, -1, null, data.Player.whoAmI, 0);
        }
        public bool TryAcceptClient(ClientPackedData data) {
            if (RuningClientsCount() + pendingClientJoins.Count >= Main.maxNetPlayers) {
                return false;
            }
            if (ReferenceEquals(Netplay.Clients[data.RemoteClient.Id], data.RemoteClient)) {
                return false;
            }
            foreach (var existingRequest in pendingClientJoins) {
                if (existingRequest == data) {
                    return false;
                }
            }
            Netplay.HasClients = true;
            pendingClientJoins.Enqueue(data);
            return true;
        }
        void ProcessClientJoins() {
            while (pendingClientJoins.TryDequeue(out var data)) {
                AcceptClientInner(data);
            }
        }
        private void AcceptClientInner(ClientPackedData data) {
            int whoAmI = data.Buffer.whoAmI;

            var player = Main.player[data.RemoteClient.Id] = data.Player;
            Netplay.Clients[data.RemoteClient.Id] = data.RemoteClient;
            Netplay.Clients[data.RemoteClient.Id].ResetSections(this);
            NetMessage.buffer[data.Buffer.whoAmI] = data.Buffer;

            NetMessage.TrySendData(MessageID.WorldData, whoAmI);
            Main.SyncAnInvasion(whoAmI);
            SendSectionsWhenJoin(whoAmI);
            SendWorldEntities(whoAmI);
            SendWorldInfo(whoAmI);

            NetMessage.TrySendData(MessageID.PlayerActive, -1, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.SyncPlayer, -1, -1, null, whoAmI);
            NetMessage.TrySendData(68, -1, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.PlayerLifeMana, -1, -1, null, whoAmI);
            NetMessage.TrySendData(42, -1, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.PlayerBuffs, -1, -1, null, whoAmI);
            NetMessage.TrySendData(MessageID.SyncLoadout, -1, -1, null, whoAmI, player.CurrentLoadoutIndex);
            for (int i = 0; i < 59; i++) {
                NetMessage.TrySendData(MessageID.SyncEquipment, -1, -1, null, whoAmI, PlayerItemSlotID.Inventory0 + i, player.inventory[i].prefix);
            }
            MessageBuffer.TrySendingItemArray(whoAmI, player.armor, PlayerItemSlotID.Armor0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.dye, PlayerItemSlotID.Dye0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.miscEquips, PlayerItemSlotID.Misc0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.miscDyes, PlayerItemSlotID.MiscDye0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.bank.item, PlayerItemSlotID.Bank1_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.bank2.item, PlayerItemSlotID.Bank2_0);
            NetMessage.TrySendData(5, -1, -1, null, whoAmI, PlayerItemSlotID.TrashItem, player.trashItem.prefix);
            MessageBuffer.TrySendingItemArray(whoAmI, player.bank3.item, PlayerItemSlotID.Bank3_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.bank4.item, PlayerItemSlotID.Bank4_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[0].Armor, PlayerItemSlotID.Loadout1_Armor_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[0].Dye, PlayerItemSlotID.Loadout1_Dye_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[1].Armor, PlayerItemSlotID.Loadout2_Armor_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[1].Dye, PlayerItemSlotID.Loadout2_Dye_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[2].Armor, PlayerItemSlotID.Loadout3_Armor_0);
            MessageBuffer.TrySendingItemArray(whoAmI, player.Loadouts[2].Dye, PlayerItemSlotID.Loadout3_Dye_0);

            NetMessage.SendData(MessageID.PlayerSpawn, whoAmI, -1, null, whoAmI, (byte)PlayerSpawnContext.SpawningIntoWorld);
            player.position = new(Main.spawnTileX * 16 + 8 - player.width / 2, Main.spawnTileY * 16 - player.height);
            player.velocity = default;
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, whoAmI, player.position.X, player.position.Y, -1);
            NetMessage.greetPlayer(whoAmI);
        }

        void SendSectionsWhenJoin(int whoAmI) {
            int spawnSectionXBegin = Terraria.Netplay.GetSectionX(Main.spawnTileX) - 2;
            int spawnSectionYBegin = Terraria.Netplay.GetSectionY(Main.spawnTileY) - 1;
            int spawnSectionXEnd = spawnSectionXBegin + 5;
            int spawnSectionYEnd = spawnSectionYBegin + 3;
            if (spawnSectionXBegin < 0) {
                spawnSectionXBegin = 0;
            }
            if (spawnSectionXEnd >= Main.maxSectionsX) {
                spawnSectionXEnd = Main.maxSectionsX;
            }
            if (spawnSectionYBegin < 0) {
                spawnSectionYBegin = 0;
            }
            if (spawnSectionYEnd >= Main.maxSectionsY) {
                spawnSectionYEnd = Main.maxSectionsY;
            }
            List<Point> existingPos = new((spawnSectionXEnd - spawnSectionXBegin) * (spawnSectionYEnd - spawnSectionYBegin));
            for (int x = spawnSectionXBegin; x < spawnSectionXEnd; x++) {
                for (int y = spawnSectionYBegin; y < spawnSectionYEnd; y++) {
                    NetMessage.SendSection(x, y, whoAmI);
                    existingPos.Add(new Point(x, y));
                }
            }
            PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, existingPos, out var portalSections);
            foreach (var section in portalSections) {
                NetMessage.SendSection(section.X, section.Y, whoAmI);
            }
        }
        void SendWorldEntities(int whoAmI) {
            NetMessage.SyncConnectedPlayer(whoAmI);
            for (int i = 0; i < Terraria.Main.maxItems; i++) {
                if (Main.item[i].active) {
                    NetMessage.TrySendData(MessageID.SyncItem, whoAmI, -1, null, i);
                    NetMessage.TrySendData(MessageID.ItemOwner, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxNPCs; i++) {
                if (Main.npc[i].active) {
                    NetMessage.TrySendData(MessageID.SyncNPC, whoAmI, -1, null, i);
                }
            }
            for (int i = 0; i < Terraria.Main.maxProjectiles; i++) {
                if (Main.projectile[i].active && (Main.projPet[Main.projectile[i].type] || Main.projectile[i].netImportant)) {
                    NetMessage.TrySendData(MessageID.SyncProjectile, whoAmI, -1, null, i);
                }
            }
        }
        void SendWorldInfo(int whoAmI) {
            for (int i = 0; i < 290; i++) {
                NetMessage.TrySendData(MessageID.NPCKillCountDeathTally, whoAmI, -1, null, i);
            }
            NetMessage.TrySendData(57, whoAmI);
            NetMessage.TrySendData(MessageID.MoonlordHorror);
            NetMessage.TrySendData(MessageID.UpdateTowerShieldStrengths, whoAmI);
            NetMessage.TrySendData(MessageID.SyncCavernMonsterType, whoAmI);
            Main.BestiaryTracker.OnPlayerJoining(this, whoAmI);
            CreativePowerManager.SyncThingsToJoiningPlayer(whoAmI);
            Main.PylonSystem.OnPlayerJoining(this, whoAmI);
            NetMessage.TrySendData(MessageID.AnglerQuest, whoAmI, -1, NetworkText.FromLiteral(Main.player[whoAmI].name), Main.anglerQuest);
        }
    }
}
