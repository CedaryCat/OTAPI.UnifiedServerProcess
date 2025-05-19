using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class AddModifiedProcessor : IFieldFilterArgProcessor {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            var unmodifiedStaticFieldFullNames = new HashSet<string>(raw.UnmodifiedStaticFieldFullNames);

            foreach (var type in raw.MainModule.GetTypes()) {
                foreach (var field in type.Fields) {
                    if (!field.IsStatic) {
                        continue;
                    }
                    if (field.IsInitOnly) {
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
