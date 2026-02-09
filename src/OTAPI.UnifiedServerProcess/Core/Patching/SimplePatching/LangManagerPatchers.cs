using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Localization;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class LangManagerPrePatcher(ILogger logger, ModuleDefinition module, MethodCallGraph callGraph) : Patcher(logger), IMethodCheckCacheFeature
    {
        public override string Name => nameof(LangManagerPrePatcher);

        public MethodCallGraph MethodCallGraph => callGraph;

        public override void Patch() {
            MethodDefinition runGame = module.GetType("Terraria.Program").GetMethod("mfwh_orig_RunGame");
            Instruction callSetLanguage = runGame.Body.Instructions
                .First(inst => inst.Operand is MethodReference { DeclaringType.Name: nameof(LanguageManager), Name: nameof(LanguageManager.SetLanguage) });
            Instruction getDefaultCulture = callSetLanguage.Previous;
            Instruction loadLangManagerInstance = callSetLanguage.Previous.Previous;


            Instruction callInitializeLegacyLocalization = runGame.Body.Instructions
                .First(inst => inst.Operand is MethodReference { DeclaringType.Name: nameof(Lang), Name: nameof(Lang.InitializeLegacyLocalization) });

            MethodDefinition globalInitialize = module
                .GetType(Constants.GlobalInitializerTypeName)
                .GetMethod(Constants.GlobalInitializerEntryPointName);

            globalInitialize.Body.Instructions.InsertRange(0, [
                loadLangManagerInstance.CloneAndClear(),
                getDefaultCulture.CloneAndClear(),
                callSetLanguage.CloneAndClear(),

                callInitializeLegacyLocalization.CloneAndClear(),
            ]);


            TypeDefinition langManager = module.GetType("Terraria.Localization.LanguageManager");
            MethodDefinition loadFilesForCulture = langManager.GetMethod("LoadFilesForCulture");

            foreach (Instruction? inst in loadFilesForCulture.Body.Instructions) {
                if (inst.Operand is MethodReference { DeclaringType.Name: "Console" } methodRef) {
                    TypeReference originalDeclaringType = methodRef.DeclaringType;
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
            TypeDefinition langManager = module.GetType("Terraria.Localization.LanguageManager");
            MethodDefinition loadFilesForCulture = langManager.GetMethod("LoadFilesForCulture");

            foreach (Instruction? inst in loadFilesForCulture.Body.Instructions) {
                if (inst.Operand is MethodReference { DeclaringType.Name: "Console_Placeholder_DontRedirect" } methodRef) {
                    methodRef.DeclaringType.Name = "Console";
                }
            }
        }
    }
}
