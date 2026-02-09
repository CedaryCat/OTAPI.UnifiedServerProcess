using Mono.Cecil;
using MonoMod.Utils;
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
    /// Create corresponding context types based on the declaring type of those static fields that need to be contextualized, and create their corresponding fields on that context.
    /// </summary>
    /// <param name="modifiedFields"></param>
    /// <param name="callGraph"></param>
    public class GenerateContextsProcessor(IReadOnlyList<FieldDefinition> modifiedFields, MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature
    {
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            ModuleDefinition module = source.MainModule;

            foreach (FieldDefinition modified in modifiedFields) {
                if (!source.OriginalToContextType.TryGetValue(modified.DeclaringType.FullName, out ContextTypeData? contextType)) {
                    contextType = new ContextTypeData(modified.DeclaringType, source.RootContextDef, callGraph.MediatedCallGraph, ref source.OriginalToContextType);
                    logger.Info("Instance-converting type: {0}", modified.DeclaringType.FullName);

                    // Redirect vanilla singleton field to converted context field
                    if (contextType.VanillaSingletonField is not null) {
                        source.OriginalToInstanceConvdField.TryAdd(contextType.VanillaSingletonField.GetIdentifier(), contextType.nestedChain.Last());
                    }
                }
                // add other fields besides singleton
                if (modified != contextType.VanillaSingletonField) {
                    if (contextType.IsReusedSingleton) {
                        var field = new FieldDefinition(modified.Name + Constants.Patching.ConvertedFieldInSingletonSuffix, modified.Attributes & ~FieldAttributes.Static, modified.FieldType);
                        field.CustomAttributes.AddRange(modified.CustomAttributes.Select(c => c.Clone()));
                        contextType.ContextTypeDef.Fields.Add(field);
                        source.OriginalToInstanceConvdField.Add(modified.GetIdentifier(), field);
                    }
                    else {
                        if (source.OriginalToInstanceConvdField.ContainsKey(modified.GetIdentifier())) {
                            continue;
                        }
                        var field = new FieldDefinition(modified.Name, modified.Attributes & ~FieldAttributes.Static, modified.FieldType);
                        field.CustomAttributes.AddRange(modified.CustomAttributes.Select(c => c.Clone()));
                        contextType.ContextTypeDef.Fields.Add(field);
                        source.OriginalToInstanceConvdField.Add(modified.GetIdentifier(), field);
                    }
                }
            }
        }
    }
}
