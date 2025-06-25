using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// Based on the full names of the fields that were modified during operation obtained from the previous analysis, 
    /// <para>search for the corresponding fields in the module and import them into the patching parameters for subsequent logic processing.</para>
    /// </summary>
    public class AddModifiedFieldsProcessor(FieldDefinition[] modifiedFields, FieldDefinition[] initModifiedFields) : IFieldFilterArgProcessor
    {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            var modifiedStaticFieldIds = new HashSet<string>(modifiedFields.Select(x => x.GetIdentifier()));
            var initialStaticFieldIds = new HashSet<string>(initModifiedFields.Select(x => x.GetIdentifier()));

            foreach (var type in raw.MainModule.GetTypes()) {
                if (type.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                foreach (var field in type.Fields) {
                    if (!field.IsStatic) {
                        continue;
                    }
                    if (field.IsLiteral) {
                        continue;
                    }
                    var id = field.GetIdentifier();
                    if (initialStaticFieldIds.Contains(id)) {
                        raw.InitialStaticFields.Add(id, field);
                    }
                    if (!modifiedStaticFieldIds.Contains(id)) {
                        raw.UnmodifiedStaticFields.Add(id, field);
                    }
                    else {
                        if (field.Name.OrdinalStartsWith("<>")) {
                            continue;
                        }
                        raw.ModifiedStaticFields.Add(id, field);
                    }
                }
            }
        }
    }
}
