using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Infrastructure
{
    [Obsolete("Not used anymore")]
    public class CacheManager(ILogger logger) : LoggedComponent(logger)
    {
        public sealed override string Name => "CacheHelper";
        const string modifiedStaticFieldCacheFile = "ModifiedStaticField.AnalysisCache.txt";
        const string initModifiedStaticFieldCacheFile = "ModifiedStaticFieldAtBegining.AnalysisCache.txt";
        public void LoadModifiedStaticFields(ModuleDefinition module, AnalyzerGroups analyzers, MethodDefinition[] entryPoint, MethodDefinition[] initOnlys, out string[] modifiedStaticFields, out string[] modifiedStaticFieldsWhenInit) {
            if (File.Exists(modifiedStaticFieldCacheFile) && File.Exists(initModifiedStaticFieldCacheFile)) {
                modifiedStaticFields = File.ReadAllLines(modifiedStaticFieldCacheFile);
                Info("Loaded cached data ({0}) from: modifiedStaticField.AnalysisCache.txt", $"count: {modifiedStaticFields.Length}");
                modifiedStaticFieldsWhenInit = File.ReadAllLines(initModifiedStaticFieldCacheFile);
                Info("Loaded cached data ({0}) from: ModifiedStaticFieldAtBegining.AnalysisCache.txt", $"count: {modifiedStaticFieldsWhenInit.Length}");
                return;
            }

            analyzers.StaticFieldModificationAnalyzer.FetchModifiedFields(entryPoint, initOnlys, out var fields, out var initOnlyFields);
            File.WriteAllLines(modifiedStaticFieldCacheFile, modifiedStaticFields = fields.Select(f => f.FullName).ToArray());
            File.WriteAllLines(initModifiedStaticFieldCacheFile, modifiedStaticFieldsWhenInit = initOnlyFields.Select(f => f.FullName).ToArray());
        }
    }
}
