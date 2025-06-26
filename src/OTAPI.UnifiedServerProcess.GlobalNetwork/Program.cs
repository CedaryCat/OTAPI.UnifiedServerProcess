using OTAPI.UnifiedServerProcess.GlobalNetwork.IO;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using ReLogic.OS;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading.Tasks;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    internal class Program
    {
        static void Main(string[] args) {
            var version = new VersionHelper();

            Console.Title = "UnifiedServerProcess v" + typeof(Program).Assembly.GetName().Version;

            Console.WriteLine(@"___________________________________________________________________________________________________");
            Console.WriteLine(@"_  _ _  _ _ ____ _ ____ ___     ____ ____ ____ _  _ ____ ____    ___  ____ ____ ____ ____ ____ ____");
            Console.WriteLine(@"|  | |\ | | |___ | |___ |  \    [__  |___ |__/ |  | |___ |__/    |__] |__/ |  | |    |___ [__  [__  ");
            Console.WriteLine(@"|__| | \| | |    | |___ |__/    ___] |___ |  \  \/  |___ |  \    |    |  \ |__| |___ |___ ___] ___]");
            Console.WriteLine(@"---------------------------------------------------------------------------------------------------");
            Console.WriteLine(@"                       Demonstration For Terraria v{0} & OTAPI v{1}                         ", version.TerrariaVersion, version.OTAPIVersion);
            Console.WriteLine(@"---------------------------------------------------------------------------------------------------");
            
            WorkRunner.RunTimedWork("Global initialization started...", () => {
                SynchronizedGuard.Load();
                NetworkPatcher.Load();
                AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.ResolveAssembly;
                Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");
                Terraria.Main.SkipAssemblyLoad = true;
                GlobalInitializer.Initialize();
            });

            var (server1, server2) = WorkRunner.RunTimedWork("Creating server instances...", () => {
                var server1 = new ServerContext("Server1", TestWorlds.World_1);
                var server2 = new ServerContext("Server2", TestWorlds.World_2);
                return (server1, server2);
            });

            var (router, cmdh) = WorkRunner.RunTimedWork("Creating global network...", () => {
                var router = new Router(7777, server1, [server1, server2]);
                var cmdh = new CommandHandler(router);
                return (router, cmdh);
            });

            WorkRunner.RunTimedWorkAsync("Starting main servers...",
            () => {
                Task.Run(() => {
                    server1.Program.LaunchGame(args);
                });
                Task.Run(() => {
                    server2.Program.LaunchGame(args);
                });
                var tcs = new TaskCompletionSource();
                router.Started += () => tcs.SetResult();
                return tcs;
            }, 
            () => {
                Console.WriteLine();
                Console.WriteLine("[USP] Unified Server Process Launched successfully.");
                Console.WriteLine("[USP] Listening on port: {0}.", router.ListenPort);
                Console.WriteLine("[USP] Type 'help' for more information.");
                Console.WriteLine();
            });

            cmdh.KeepReadingInput();
        }
    }
}
