using OTAPI.UnifiedServerProcess.GlobalNetwork.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.IO;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.Servers
{
    public class ServerContext : RootContext
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public ServerContext(string worldName, byte[] worldFileData, int port) : base(worldName) {
            Console = new ConsoleClientLauncher(this);

            var worldPath = Terraria.Main.GetWorldPathFromName(worldName, false);
            File.WriteAllBytes(worldPath, worldFileData);
            Main.ActiveWorldFileData = WorldFile.GetAllMetadata(worldPath, false);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = port;
            Netplay.UseUPNP = true;
        }
    }
}
