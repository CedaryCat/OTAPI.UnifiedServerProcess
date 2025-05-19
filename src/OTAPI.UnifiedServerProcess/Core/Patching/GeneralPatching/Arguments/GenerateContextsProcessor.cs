using Mono.Cecil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments {
    public class GenerateContextsProcessor(IReadOnlyList<FieldDefinition> modifiedFields, MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature {
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            var module = source.MainModule;

            foreach (var modified in modifiedFields) {
                if (!source.OriginalToContextType.TryGetValue(modified.DeclaringType.FullName, out var contextType)) {
                    contextType = new ContextTypeData(modified.DeclaringType, source.RootContextDef, callGraph.MediatedCallGraph, ref source.OriginalToContextType);
                    logger.Info("Instance-converting type: {0}", modified.DeclaringType.FullName);

                    // Redirect vanilla singleton field to converted context field
                    if (contextType.VanillaSingletonField is not null) {
                        source.OriginalToInstanceConvdField.TryAdd(contextType.VanillaSingletonField.FullName, contextType.nestedChain.Last());
                    }
                }
                // add other fields besides singleton
                if (modified != contextType.VanillaSingletonField) {
                    if (contextType.IsReusedSingleton) {
                        var field = new FieldDefinition(modified.Name + Constants.Patching.ConvertedFieldInSingletonSuffix, modified.Attributes & ~FieldAttributes.Static, modified.FieldType);
                        field.CustomAttributes.AddRange(modified.CustomAttributes.Select(c => c.Clone()));
                        contextType.ContextTypeDef.Fields.Add(field);
                        source.OriginalToInstanceConvdField.Add(modified.FullName, field);
                    }
                    else {
                        var field = new FieldDefinition(modified.Name, modified.Attributes & ~FieldAttributes.Static, modified.FieldType);
                        field.CustomAttributes.AddRange(modified.CustomAttributes.Select(c => c.Clone()));
                        contextType.ContextTypeDef.Fields.Add(field);
                        source.OriginalToInstanceConvdField.Add(modified.FullName, field);
                    }
                }
            }
        }
    }
}
