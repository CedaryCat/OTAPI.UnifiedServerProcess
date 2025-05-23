using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments {
    /// <summary>
    /// Redirects System.Console.* to UnifiedServerProcess.ConsoleSystem to achieve better console IO expansion
    /// </summary>
    /// <param name="callGraph"></param>
    public class ConsoleRedirectProcessor(MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature {
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            var module = source.MainModule;
            var console = new TypeReference(
                nameof(System),
                nameof(System.Console),
                source.MainModule,
                module.AssemblyReferences.First(a => a.Name == "System.Console")).Resolve();

            var consoleSystem = source.MainModule.GetType("UnifiedServerProcess.ConsoleSystemContext");

            var predefined = ContextTypeData.Predefine(console, consoleSystem, [source.RootContextDef.Field("Console")]);

            foreach (var kv in predefined.PredefinedMethodMap) {
                var method = kv.Value;
                this.AddPredefineMethodUsedContext(kv.Key);
            }

            source.OriginalToContextType.Add(console.FullName, predefined);
        }
    }
}
