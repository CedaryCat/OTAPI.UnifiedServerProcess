using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using System.Net;
using Terraria;
using Terraria.Utilities;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Network
{
    public static class NetworkPatcher
    {
        static NetworkPatcher() {
            On.Terraria.NetplaySystemContext.StartServer += Modified_StartServer;
            On.Terraria.NetplaySystemContext.StartBroadCasting += Modified_StartBroadCasting;
            On.Terraria.NetplaySystemContext.StopBroadCasting += Modified_StopBroadCasting;
        }

        private static void Modified_StartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, NetplaySystemContext self) {
            self.Connection.ResetSpecialFlags();
            self.ResetNetDiag();

            var context = self.root;
            var Main = context.Main;

            Main.rand ??= new UnifiedRandom((int)DateTime.Now.Ticks);
            Main.myPlayer = 255;
            self.ServerIP = IPAddress.Any;
            Main.menuMode = 14;
            Main.statusText = Lang.menu[8].Value;
            Main.netMode = 2;
            self.Disconnect = false;
            for (int i = 0; i < 256; i++) {
                self.Clients[i] = new RemoteClient(context);
                self.Clients[i].Reset(context);
                self.Clients[i].Id = i;
                self.Clients[i].ReadBuffer = new byte[1024];
            }

            if (self.root is ServerContext server) {
                server.IsRunning = true;
            }
        }
        private static void Modified_StartBroadCasting(On.Terraria.NetplaySystemContext.orig_StartBroadCasting orig, NetplaySystemContext self) { }
        private static void Modified_StopBroadCasting(On.Terraria.NetplaySystemContext.orig_StopBroadCasting orig, NetplaySystemContext self) { }

        // Just triggers the static constructor
        public static void Load() { }
    }
}
