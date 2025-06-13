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
        public readonly Guid UniqueId = Guid.NewGuid();

        public bool IsRunning;
        public ServerContext(string worldName, byte[] worldFileData) : base(worldName) {
            Console = new ConsoleClientLauncher(this);

            var worldPath = Path.Combine(Terraria.Main.WorldPath, worldName);
            File.WriteAllBytes(worldPath, worldFileData);
            Main.ActiveWorldFileData = WorldFile.GetAllMetadata(worldPath, false);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = -1;
            Netplay.UseUPNP = true;
        }
    }
}
