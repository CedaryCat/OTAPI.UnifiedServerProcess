using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    /// <summary>
    /// Redirects System.Console.* to UnifiedServerProcess.ConsoleSystem to achieve better console IO expansion
    /// </summary>
    /// <param name="callGraph"></param>
    public class ConsoleRedirectProcessor(MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature
    {
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            ModuleDefinition module = source.MainModule;
            TypeDefinition console = new TypeReference(
                nameof(System),
                nameof(System.Console),
                source.MainModule,
                module.AssemblyReferences.First(a => a.Name == "System.Console")).Resolve();

            TypeDefinition consoleSystem = source.MainModule.GetType("UnifiedServerProcess.ConsoleSystemContext");

            var predefined = ContextTypeData.Predefine(console, consoleSystem, [source.RootContextDef.GetField("Console")]);

            foreach (KeyValuePair<string, MethodDefinition> kv in predefined.PredefinedMethodMap) {
                MethodDefinition method = kv.Value;
                this.AddPredefineMethodUsedContext(kv.Key);
            }

            source.OriginalToContextType.Add(console.FullName, predefined);
        }
    }
}
