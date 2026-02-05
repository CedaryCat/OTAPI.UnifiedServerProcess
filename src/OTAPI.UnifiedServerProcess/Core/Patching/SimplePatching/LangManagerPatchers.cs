using ModFramework;
using Mono.Cecil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class LangManagerPrePatcher(ILogger logger, ModuleDefinition module, MethodCallGraph callGraph) : Patcher(logger), IMethodCheckCacheFeature
    {
        public override string Name => nameof(LangManagerPrePatcher);

        public MethodCallGraph MethodCallGraph => callGraph;

        public override void Patch() {
            var runGame = module.GetType("Terraria.Program").GetMethod("mfwh_orig_RunGame");
            var callSetLanguage = runGame.Body.Instructions
                .First(inst => inst.Operand is MethodReference { DeclaringType.Name: "LanguageManager", Name: "SetLanguage" });
            var getDefaultCulture = callSetLanguage.Previous;
            var loadLangManagerInstance = callSetLanguage.Previous.Previous;

            runGame.Body.Instructions.Remove(loadLangManagerInstance);
            runGame.Body.Instructions.Remove(getDefaultCulture);
            runGame.Body.Instructions.Remove(callSetLanguage);

            var globalInitialize = module
                .GetType(Constants.GlobalInitializerTypeName)
                .GetMethod(Constants.GlobalInitializerEntryPointName);
            globalInitialize.Body.Instructions.InsertRange(0, [
                loadLangManagerInstance,
                getDefaultCulture,
                callSetLanguage,
            ]);


            var langManager = module.GetType("Terraria.Localization.LanguageManager");
            var loadFilesForCulture = langManager.GetMethod("LoadFilesForCulture");

            foreach (var inst in loadFilesForCulture.Body.Instructions) {
                if (inst.Operand is MethodReference { DeclaringType.Name: "Console" } methodRef) {
                    var originalDeclaringType = methodRef.DeclaringType;
                    var declaringType = new TypeReference(originalDeclaringType.Namespace, originalDeclaringType.Name, originalDeclaringType.Module, originalDeclaringType.Scope);
                    var cloned = new MethodReference(methodRef.Name, methodRef.ReturnType, declaringType) { HasThis = methodRef.HasThis };
                    cloned.Parameters.AddRange(methodRef.Parameters.Select(x => x.Clone()));
                    cloned.DeclaringType.Name = "Console_Placeholder_DontRedirect";
                    inst.Operand = cloned;
                }
            }

            this.ForceOverrideContextBoundCheck(loadFilesForCulture, false);
        }
    }
    public class LangManagerPostPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(LangManagerPostPatcher);

        public override void Patch() {
            var langManager = module.GetType("Terraria.Localization.LanguageManager");
            var loadFilesForCulture = langManager.GetMethod("LoadFilesForCulture");

            foreach (var inst in loadFilesForCulture.Body.Instructions) {
                if (inst.Operand is MethodReference { DeclaringType.Name: "Console_Placeholder_DontRedirect" } methodRef) {
                    methodRef.DeclaringType.Name = "Console";
                }
            }
        }
    }
}
