using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using ReLogic.OS;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    internal class Program
    {
        static void Main(string[] args) {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.ResolveAssembly;
            Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");

            NetworkPatcher.Load();

            var server1 = new ServerContext("Server1", TestWorlds.World_1);
            Task.Run(() => {
                server1.Main.SkipAssemblyLoad = true;
                server1.Program.LaunchGame(args);
            });

            var server2 = new ServerContext("Server2", TestWorlds.World_2);
            Task.Run(() => {
                server2.Main.SkipAssemblyLoad = true;
                server2.Program.LaunchGame(args);
            });

            var router = new Router(7777, server1, [server1, server2]);

            while (true) {
                Console.ReadLine();
            }
        }
    }
}
