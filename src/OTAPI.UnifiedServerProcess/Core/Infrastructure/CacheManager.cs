using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Infrastructure {
    public class CacheManager(ILogger logger) : LoggedComponent(logger) {
        public sealed override string Name => "CacheHelper";
        const string unmodifiedStaticFieldCacheFile = "UnmodifiedStaticField.AnalysisCache.txt";
        const string modifiedStaticFieldCacheFile = "ModifiedStaticField.AnalysisCache.txt";
        public string[] LoadReadonlyStaticFields(ModuleDefinition module, AnalyzerGroups analyzers, params MethodDefinition[] entryPoints) {
            if (File.Exists(unmodifiedStaticFieldCacheFile)) {
                var result = File.ReadAllLines(unmodifiedStaticFieldCacheFile);
                Info("Loaded cached data ({0}) from: UnmodifiedStaticField.AnalysisCache.txt", $"count: {result.Length}");
                return result;
            }

            var fields = analyzers.StaticFieldModificationAnalyzer.FetchModifiedFields(entryPoints);
            File.WriteAllLines(modifiedStaticFieldCacheFile, fields.Select(f => f.FullName));

            HashSet<string> modifiedFields = [.. fields.Select(f => f.FullName)];
            List<FieldDefinition> unmodifiedStaticFields = [];
            foreach (var type in module.GetTypes()) {
                if (type.Name.StartsWith('<')) {
                    continue;
                }
                foreach (var field in type.Fields) {
                    if (!field.IsStatic) {
                        continue;
                    }
                    //if (field.IsInitOnly) {
                    //    continue;
                    //}
                    if (field.IsLiteral) {
                        continue;
                    }
                    if (modifiedFields.Contains(field.FullName)) {
                        continue;
                    }
                    unmodifiedStaticFields.Add(field);
                }
            }
            var fullNames = unmodifiedStaticFields.Select(f => f.FullName).ToArray();
            File.WriteAllLines(unmodifiedStaticFieldCacheFile, fullNames);

            return fullNames;
        }
    }
}
