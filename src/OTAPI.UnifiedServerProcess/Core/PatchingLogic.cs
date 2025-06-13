using ModFramework;
using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Infrastructure;
using OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core
{
    public static class PatchingLogic
    {
        public static void Patch(ILogger logger, ModuleDefinition module) {

            var analyzers = new AnalyzerGroups(logger, module);
            var cacheHelper = new CacheManager(logger);

            var main = module.GetType("Terraria.Main");

            var unmodifiedStaticFields = cacheHelper.LoadUnmodifiedStaticFields(module, analyzers,
                [main.Method("Initialize_TileAndNPCData1"), main.Method("Initialize_TileAndNPCData2")],
                main.Method(".ctor"),
                main.Method("DedServ"));

            List<FieldDefinition> modifiedStaticFields = [];
            var rootContextDef = module.GetType(Constants.RootContextFullName);

            new PatchChain(logger)
                .Then(new SimplifyMacrosPatcher(logger, module))
                .Then(new RemoveUnusedCodePatcherAtBegin(logger, module))

                .DefineArgument(new FilterArgumentSource(module, unmodifiedStaticFields))
                .RegisterProcessor(new AddModifiedFieldsProcessor())
                .RegisterProcessor(new AddEventsProcessor())
                .RegisterProcessor(new AddHooksProcessor())
                .RegisterProcessor(new ForceStaticProcessor())
                .RegisterProcessor(new StaticGenericProcessor())
                .RegisterProcessor(new ContextRequiredFieldsProcessor(analyzers.MethodCallGraph, rootContextDef))
                .ApplyArgument()
                .Finalize((fieldArgument) => {
                    modifiedStaticFields.AddRange(fieldArgument.ModifiedStaticFields);
                })

                .DefineArgument(new PatcherArgumentSource(module, rootContextDef))
                .RegisterProcessor(new ConsoleRedirectProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new GenerateContextsProcessor(modifiedStaticFields, analyzers.MethodCallGraph))
                .RegisterProcessor(new PreparePropertiesProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new ExternalInterfaceProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new StaticConstructorProcessor(analyzers.MethodCallGraph))
                .ApplyArgument()
                .Then(new AdjustHooksPatcher(logger, analyzers.MethodCallGraph))
                .Then(new StaticRedirectPatcher(logger, analyzers.DelegateInvocationGraph, analyzers.MethodInheritanceGraph, analyzers.MethodCallGraph))
                .Then(new CctorCtxAdaptPatcher(logger))
                .Then(new HooksCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new InvocationCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new EnumeratorCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new CleanupCtxUnboundPatcher(logger))
                .Then(new AdjustPropertiesPatcher(logger, analyzers.MethodCallGraph))
                .Then(new AdjustEventsPatcher(logger))
                .Then(new AdjustAccessModifiersPatcher(logger))
                .Then(new ContextInstantiationOrderPatcher(logger, analyzers.MethodCallGraph))
                .Then(new TrivialDefaultValuePatcher(logger))
                .Finalize()

                .Then(new AdjustAutoPropertiesPatcher(logger, module))
                .Then(new RemoveUnusedCodePatcherAtEnd(logger, rootContextDef, module))
                .Then(new OptimizeMacrosPatcher(logger, module))

                .Execute();
        }
    }
}
