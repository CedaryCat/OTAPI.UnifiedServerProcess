using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// Based on the full names of the fields that were modified during operation obtained from the previous analysis, 
    /// <para>search for the corresponding fields in the module and import them into the patching parameters for subsequent logic processing.</para>
    /// </summary>
    public class AddModifiedFieldsProcessor() : IFieldFilterArgProcessor
    {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            var unmodifiedStaticFieldFullNames = new HashSet<string>(raw.UnmodifiedStaticFieldFullNames);

            foreach (var type in raw.MainModule.GetTypes()) {
                if (type.Name.StartsWith('<')) {
                    continue;
                }
                foreach (var field in type.Fields) {
                    if (!field.IsStatic) {
                        continue;
                    }
                    if (field.IsLiteral) {
                        continue;
                    }
                    if (unmodifiedStaticFieldFullNames.Contains(field.FullName)) {
                        raw.UnmodifiedStaticFields.Add(field);
                    }
                    else {
                        if (field.Name.StartsWith("<>")) {
                            continue;
                        }
                        raw.ModifiedStaticFields.Add(field);
                    }
                }
            }
        }
    }
}
