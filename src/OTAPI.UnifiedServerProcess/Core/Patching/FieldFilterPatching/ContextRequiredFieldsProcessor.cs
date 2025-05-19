using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class ContextRequiredFieldsProcessor(MethodCallGraph callGraph, TypeDefinition rootContextDef) : IFieldFilterArgProcessor, IMethodCheckCacheFeature {
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var field in source.UnmodifiedStaticFields.ToArray()) {
                ProcessField(field, source);
            }
        }

        private void ProcessField(FieldDefinition field, FilterArgumentSource source) {
            var fieldType = field.FieldType;
            if (fieldType is ArrayType) {
                return;
            }
            var ctors = fieldType.TryResolve()?.GetConstructors()?.ToArray() ?? [];
            foreach (var ctor in ctors) {
                if (ctor.IsStatic) {
                    continue;
                }
                if (this.CheckUsedContextBoundField(rootContextDef, source.ModifiedStaticFields, ctor, false)) {
                    source.UnmodifiedStaticFields.Remove(field);
                    source.ModifiedStaticFields.Add(field);
                    return;
                }
            }
        }
    }
}
