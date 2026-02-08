using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core
{
    public static class PatchingLogic
    {
        public static void Patch(ILogger logger, ModuleDefinition module) {

            new PatchProjHookSets(module).Patch();
            new PatchCollision(module).Patch();
            new NetworkLogicPruner(module).Prune("Terraria.Player");

            var analyzers = new AnalyzerGroups(logger, module);
            // var cacheHelper = new CacheManager(logger);

            var main = module.GetType("Terraria.Main");

            MethodDefinition[] entryPoints = [main.GetMethod("DedServ")];
            MethodDefinition[] initialMethods = [main.GetMethod("Initialize"), main.GetMethod("Initialize_AlmostEverything"), main.GetMethod("PostContentLoadInitialize")];

            analyzers.StaticFieldModificationAnalyzer.FetchModifiedFields(
                entryPoints, initialMethods,
                out var rawModifiedStaticFields, out var initialStaticFields);

            List<FieldDefinition> modifiedStaticFields = [];
            var rootContextDef = module.GetType(Constants.RootContextFullName);

            new PatchChain(logger)
                .Then(new SimplifyMacrosPatcher(logger, module))
                .Then(new RemoveUnusedCodePatcherAtBegin(logger, module, analyzers.MethodCallGraph))
                .Then(new LangManagerPrePatcher(logger, module, analyzers.MethodCallGraph))
                .Then(new SetThreadStatePatcher(logger, module, analyzers.MethodCallGraph))

                .DefineArgument(new FilterArgumentSource(module, initialMethods))
                .RegisterProcessor(new AddModifiedFieldsProcessor(rawModifiedStaticFields, initialStaticFields))
                .RegisterProcessor(new AddEventsProcessor())
                .RegisterProcessor(new AddHooksProcessor())
                .RegisterProcessor(new ServerNetmodeProcessor())
                .RegisterProcessor(new ForceStaticProcessor())
                .RegisterProcessor(new ForceInstanceProcessor())
                .RegisterProcessor(new StaticGenericProcessor())
                .RegisterProcessor(new ContextRequiredFieldsProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new InitialFieldModificationProcessor(logger, analyzers))
                .ApplyArgument()
                .Finalize((fieldArgument) => {
                    modifiedStaticFields.AddRange(fieldArgument.ModifiedStaticFields);
                })

                .DefineArgument(new PatcherArgumentSource(module, rootContextDef))
                .RegisterProcessor(new ConsoleRedirectProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new DelegatePlaceholderProcessor(analyzers))
                .RegisterProcessor(new GenerateContextsProcessor(modifiedStaticFields, analyzers.MethodCallGraph))
                .RegisterProcessor(new PreparePropertiesProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new ExternalInterfaceProcessor(analyzers.MethodCallGraph))
                .RegisterProcessor(new StaticConstructorProcessor(analyzers))
                .RegisterProcessor(new NewConstraintInjectCtxProcessor(analyzers))
                .ApplyArgument()
                .Then(new AdjustHooksPatcher(logger, analyzers.MethodCallGraph))
                .Then(new StaticRedirectPatcher(logger, analyzers.DelegateInvocationGraph, analyzers.MethodInheritanceGraph, analyzers.MethodCallGraph))
                .Then(new DelegateWithCtxParamPatcher(logger))
                .Then(new CctorCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new HooksCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new InvocationCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new EnumeratorCtxAdaptPatcher(logger, analyzers.MethodCallGraph))
                .Then(new CleanupCtxUnboundPatcher(logger))
                .Then(new AdjustPropertiesPatcher(logger, analyzers.MethodCallGraph))
                .Then(new AdjustEventsPatcher(logger))
                .Then(new AdjustAccessModifiersPatcher(logger))
                .Then(new ContextInstantiationOrderPatcher(logger, analyzers.MethodCallGraph, analyzers.TypeInheritanceGraph))
                .Then(new TrivialDefaultValuePatcher(logger))
                .Finalize()

                .Then(new ThreadLocalInitPatcher(logger, module))
                .Then(new AdjustAutoPropertiesPatcher(logger, module))
                .Then(new LangManagerPostPatcher(logger, module))
                .Then(new RemoveUnusedCodePatcherAtEnd(logger, rootContextDef, module))
                .Then(new OptimizeMacrosPatcher(logger, module))
                .Then(new InitLocalsFixPatcher(logger, module))

                .Execute();
        }
    }
}
