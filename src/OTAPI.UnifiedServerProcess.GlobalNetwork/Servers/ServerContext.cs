using OTAPI.UnifiedServerProcess.GlobalNetwork.CLI;
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
        public override string ToString() => $"{{ Type:ServerContext, Name:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";
    }
}
