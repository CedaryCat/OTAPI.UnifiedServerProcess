using ModFramework;
using ModFramework.Relinker;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using OTAPI.UnifiedServerProcess.Loggers.Implements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OTAPI.UnifiedServerProcess
{
    public class PatchExecutor
    {
        static PatchExecutor() => PatchMonoMod();
        /// <summary>
        /// Current MonoMod is outdated, and the new reorg is not ready yet, however we need v25 RD for NET9, yet Patcher v22 is the latest, and is not compatible with v25.
        /// Ultimately the problem is OTAPI using both relinker+rd at once.
        /// For now, the intention is to replace the entire both with "return new string[0];" to prevent the GAC IL from being used (which it isn't anyway)
        /// </summary>
        public static void PatchMonoMod() {
            var bin = File.ReadAllBytes("MonoMod.dll");
            using MemoryStream ms = new(bin);
            var asm = AssemblyDefinition.ReadAssembly(ms);
            var modder = asm.MainModule.Types.Single(x => x.FullName == "MonoMod.MonoModder");
            var gacPaths = modder.Methods.Single(m => m.Name == "get_GACPaths");
            var il = gacPaths.Body.GetILProcessor();
            if (il.Body.Instructions.Count != 3) {
                il.Body.Instructions.Clear();
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, asm.MainModule.ImportReference(typeof(string)));
                il.Emit(OpCodes.Ret);

                // clear MonoModder.MatchingConditionals(cap, asmName), with "return false;"
                var mc = modder.Methods.Single(m => m.Name == "MatchingConditionals" && m.Parameters.Count == 2 && m.Parameters[1].ParameterType.Name == "AssemblyNameReference");
                il = mc.Body.GetILProcessor();
                mc.Body.Instructions.Clear();
                mc.Body.Variables.Clear();
                mc.Body.ExceptionHandlers.Clear();
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);

                var writerParams = modder.Methods.Single(m => m.Name == "get_WriterParameters");
                il = writerParams.Body.GetILProcessor();
                var get_Current = writerParams.Body.Instructions.Single(x => x.Operand is MethodReference mref && mref.Name == "get_Current");
                // replace get_Current with a number, and remove the bitwise checks
                il.Remove(get_Current.Next);
                il.Remove(get_Current.Next);
                il.Replace(get_Current, Instruction.Create(
                    OpCodes.Ldc_I4, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 37 : 0
                ));

                asm.Write("MonoMod.dll");
            }
        }

        public bool Patch() {
            DirectoryInfo outputDir = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "output"));
            outputDir.Create();
            var output = Path.Combine(outputDir.FullName, "OTAPI.dll");
            var hookOutput = Path.Combine(outputDir.FullName, "OTAPI.Runtime.dll");

            var input = typeof(Terraria.Main).Assembly.Location;

            var modcontext = new ModContext("Terraria");
            modcontext.ReferenceFiles.AddRange(new[]
            {
                "ModFramework.dll",
                "MonoMod.dll",
                "MonoMod.Utils.dll",
                "MonoMod.RuntimeDetour.dll",
                "Mono.Cecil.dll",
                "Mono.Cecil.Rocks.dll",
                "Newtonsoft.Json.dll",
                "Steamworks.NET.dll",
                input,
                typeof(Program).Assembly.Location,
            });

            using ModFwModder mm = new(modcontext) {
                InputPath = input,
                OutputPath = output,
                MissingDependencyThrow = false,
                PublicEverything = false,
                LogVerboseEnabled = true,
                GACPaths = new string[] { }, // avoid MonoMod looking up the GAC, which causes an exception on .netcore
            };

            List<MethodDefinition> virtualMaked = new List<MethodDefinition>();

            var embeddedResources = modcontext.ExtractResources(input);

            var pluginsPath = Path.Combine(modcontext.BaseDirectory, "modifications");
            Directory.CreateDirectory(pluginsPath);
            modcontext.PluginLoader.AddFromFolder(pluginsPath);

            var logger = new DefaultLogger(Logger.DEBUG);

            _ = new ModAssemblyMerger(modcontext, typeof(TrProtocol.MessageID).Assembly);

            modcontext.OnApply += (modType, modder) => {

                if (modder is not null) {
                    if (modType == ModType.PreRead) {
                        modder.AssemblyResolver.AddSearchDirectory(embeddedResources);
                        modder.AddTask<CoreLibRelinker>();
                    }
                    else if (modType == ModType.Read) {
                    }
                    else if (modType == ModType.PreWrite) {
                        PatchingLogic.Patch(logger, modder.Module);
                        modder.ModContext.TargetAssemblyName = "OTAPI";
                    }
                    else if (modType == ModType.Write) {
                        modder.ModContext = new("OTAPI.Runtime");
                        modder.CreateRuntimeHooks(hookOutput);
                    }
                }

                return ModContext.EApplyResult.Continue;
            };

            var status = "OTAPI";

            mm.Read();
            mm.MapDependencies();
            mm.AutoPatch();

            Console.WriteLine($"[OTAPI-ProC] Writing: {status}, Path={new Uri(Environment.CurrentDirectory).MakeRelativeUri(new(mm.OutputPath))}");

            mm.Write();

            return true;
        }
    }
}
