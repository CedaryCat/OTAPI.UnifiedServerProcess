using ModFramework;
using ModFramework.Relinker;
using Mono.Cecil;
using System.IO;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    [MonoMod.MonoModIgnore]
    public static class ModFwModderExt
    {
        [MonoMod.MonoModIgnore]
        public static void CreateRuntimeHooks(this ModFwModder modder, string output) {
            modder.Log("[OTAPI-ProC] Generating OTAPI.Runtime.dll");
            var gen = new MonoMod.RuntimeDetour.HookGen.HookGenerator(modder, "OTAPI.Runtime.dll");
            using var srm = new MemoryStream();
            using (ModuleDefinition mOut = gen.OutputModule) {
                gen.Generate();

                mOut.Write(srm);
            }

            srm.Position = 0;
            var fileName = Path.GetFileName(output);
            using var mm = new ModFwModder(new("OTAPI.Runtime")) {
                Input = srm,
                OutputPath = output,
                MissingDependencyThrow = false,
                //LogVerboseEnabled = true,
                // PublicEverything = true, // this is done in setup

                GACPaths = new string[] { } // avoid MonoMod looking up the GAC, which causes an exception on .netcore
            };
            mm.Log($"[OTAPI-ProC] Processing corelibs to be net9: {fileName}");

            mm.Read();

            mm.AddTask<CoreLibRelinker>();

            mm.MapDependencies();
            mm.AutoPatch();

            mm.Write();
        }
    }
}
