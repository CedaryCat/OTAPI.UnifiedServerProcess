
using ReLogic.OS;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.Test
{
    internal class Program
    {
        static void Main(string[] args) {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.ResolveAssembly;
            Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");
            Terraria.Main.SkipAssemblyLoad = true;

            RootContext test;
            for (int i = 0; i < 1200; i++) {
                test = new RootContext("Test");
            }

            //test.Hooks.NetMessage.PlayerAnnounce += (sender, e) => {
            //    Console.WriteLine("[USP] Player joined: " + test.Main.player[e.Plr].name);
            //};

            //test.Program.LaunchGame(args);
        }
    }
}
