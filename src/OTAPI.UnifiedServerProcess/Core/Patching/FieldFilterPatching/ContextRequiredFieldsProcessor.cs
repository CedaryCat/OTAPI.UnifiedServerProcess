using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// Promotes otherwise-unmodified static fields only when their actual static-initialization
    /// value or control flow depends on context-bound state.
    /// </summary>
    /// <param name="callGraph"></param>
    public class ContextRequiredFieldsProcessor(MethodCallGraph callGraph) : IFieldFilterArgProcessor, IInitializationDependencyCheckFeature
    {
        public string Name => nameof(ContextRequiredFieldsProcessor);
        public MethodCallGraph MethodCallGraph => callGraph;

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            List<(MethodDefinition Method, Instruction Store, string FieldId)> initializationSites = [];
            foreach (TypeDefinition type in source.MainModule.GetAllTypes().ToArray()) {
                MethodDefinition? staticConstructor = type.GetStaticConstructor();
                if (staticConstructor is null) {
                    continue;
                }
                foreach (Instruction instruction in staticConstructor.Body.Instructions) {
                    if (instruction.OpCode != OpCodes.Stsfld
                        || instruction.Operand is not FieldReference fieldReference
                        || fieldReference.TryResolve() is not FieldDefinition field) {
                        continue;
                    }
                    string fieldId = field.GetIdentifier();
                    if (source.UnmodifiedStaticFields.ContainsKey(fieldId)) {
                        initializationSites.Add((staticConstructor, instruction, fieldId));
                    }
                }
            }

            bool incremented;
            do {
                incremented = false;
                foreach ((MethodDefinition method, Instruction store, string fieldId) in initializationSites) {
                    if (!source.UnmodifiedStaticFields.TryGetValue(fieldId, out FieldDefinition? field)) {
                        continue;
                    }
                    if (!this.IsContextDependentInitialization(
                        source.ModifiedStaticFields,
                        method,
                        [store])) {
                        continue;
                    }

                    source.UnmodifiedStaticFields.Remove(fieldId);
                    source.ModifiedStaticFields.TryAdd(fieldId, field);
                    incremented = true;
                }
            }
            while (incremented);
        }
    }
}
