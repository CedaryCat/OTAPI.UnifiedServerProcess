using OTAPI.UnifiedServerProcess.GlobalNetwork.IO;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Network;
using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using ReLogic.OS;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    internal class Program
    {
        static void Main(string[] args) {
            var version = new VersionHelper();

            Console.WriteLine("___________________________________________________________________________________________________");
            Console.WriteLine("_  _ _  _ _ ____ _ ____ ___     ____ ____ ____ _  _ ____ ____    ___  ____ ____ ____ ____ ____ ____");
            Console.WriteLine("|  | |\\ | | |___ | |___ |  \\    [__  |___ |__/ |  | |___ |__/    |__] |__/ |  | |    |___ [__  [__  ");
            Console.WriteLine("|__| | \\| | |    | |___ |__/    ___] |___ |  \\  \\/  |___ |  \\    |    |  \\ |__| |___ |___ ___] ___]");
            Console.WriteLine("---------------------------------------------------------------------------------------------------");
            Console.WriteLine("                       Demonstration For Terraria v{0} & OTAPI v{1}                         ", version.TerrariaVersion, version.OTAPIVersion);
            Console.WriteLine("---------------------------------------------------------------------------------------------------");

            Console.Write("[USP|Info] Waiting for servers instances creation... ");
            var spinner = new ConsoleSpinner(100);
            spinner.Start();

            SynchronizedGuard.Load();
            NetworkPatcher.Load();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.ResolveAssembly;
            Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");
            Terraria.Main.SkipAssemblyLoad = true;
            GlobalInitializer.InitializeEntryPoint();

            int port = 7777;

            var server1 = new ServerContext("Server1", TestWorlds.World_1);
            var server2 = new ServerContext("Server2", TestWorlds.World_2);

            var router = new Router(port, server1, [server1, server2]);
            var cmd = new CommandHandler(router);

            spinner.Stop();
            Console.WriteLine("- done.");

            Task.Run(() => {
                server1.Program.LaunchGame(args);
            });
            Task.Run(() => {
                server2.Program.LaunchGame(args);
            });


            Console.Write("[USP|Info] Waiting for main servers to start... ");
            spinner = new ConsoleSpinner(100);
            spinner.Start();

            router.Started += () => {
                spinner.Stop();
                Console.WriteLine("- done.");
                Console.WriteLine();
                Console.WriteLine("[USP] Unified Server Process Launched successfully.");
                Console.WriteLine("[USP] Listening on port: {0}.", port);
                Console.WriteLine("[USP] Type 'help' for more information.");
                Console.WriteLine();
            };

            cmd.KeepReadingInput();
        }
    }
}
