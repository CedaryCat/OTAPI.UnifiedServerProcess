using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments {
    public class PreparePropertiesProcessor(MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature {
        public MethodCallGraph MethodCallGraph => callGraph;
        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {

            var convertedTypes = source.OriginalToContextType.Values.ToDictionary(t => t.ContextTypeDef.FullName, t => t.ContextTypeDef).ToImmutableDictionary();
            foreach (var type in source.MainModule.GetAllTypes().ToArray()) {
                if (type.Name.StartsWith('<')) {
                    continue;
                }
                if (ForceStaticProcessor.forceStaticTypeFullNames.Contains(type.FullName)) {
                    continue;
                }
                if (source.OriginalToContextType.ContainsKey(type.FullName) || convertedTypes.ContainsKey(type.FullName)) {
                    continue;
                }
                foreach (var prop in type.Properties) {
                    if (prop.HasThis) {
                        continue;
                    }

                    if (prop.GetMethod is not null && prop.GetMethod.DeclaringType is null) {
                        prop.GetMethod.DeclaringType = type;
                    }
                    if (prop.SetMethod is not null && prop.SetMethod.DeclaringType is null) {
                        prop.SetMethod.DeclaringType = type;
                    }

                    if ((prop.GetMethod is not null && this.CheckUsedContextBoundField(source.RootContextDef, source.OriginalToInstanceConvdField, prop.GetMethod))
                        || (prop.SetMethod is not null && this.CheckUsedContextBoundField(source.RootContextDef, source.OriginalToInstanceConvdField, prop.SetMethod))) {

                        _ = new ContextTypeData(prop.DeclaringType, source.RootContextDef, callGraph.MediatedCallGraph, ref source.OriginalToContextType);
                        logger.Info("Instance-converting type: {0}", prop.DeclaringType.FullName);
                        break;
                    }
                }
            }
        }
    }
}
