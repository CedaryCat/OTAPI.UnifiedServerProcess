using ReLogic.OS;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.Test {
    internal class Program
    {
        static void Main(string[] args) {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.ResolveAssembly;
            Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");

            var test = new RootContext("Test");

            test.Hooks.NetMessage.PlayerAnnounce += (sender, e) => {
                Console.WriteLine("[USP] Player joined: " + test.Main.player[e.Plr].name);
            };

            test.Main.SkipAssemblyLoad = true; 
            test.Program.LaunchGame(args);
        }
    }
}
