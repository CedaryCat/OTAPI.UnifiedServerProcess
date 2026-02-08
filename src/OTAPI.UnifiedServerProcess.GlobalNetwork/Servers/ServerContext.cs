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
        public Thread? RunningThread { get; protected set; }
        public virtual Thread Run(string[] args) {
            Thread result = RunningThread = new Thread(() => RunBlocking(args)) {
                IsBackground = true,
            };
            result.Name = $"Server Instance: {Name}";
            result.Start();
            return result;
        }
        public virtual void RunBlocking(string[] args) {
            ThreadLocalInitializer.Initialize();
            RunningThread = Thread.CurrentThread;
            RunningThread.Name = $"Server Instance: {Name}";
            Program.LaunchGame(args);
        }
        public override string ToString() => $"{{ Type:ServerContext, Name:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";
    }
}
