using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class AddEventsProcessor : IFieldFilterArgProcessor {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            foreach (var type in raw.MainModule.GetTypes()) {
                if (type.Namespace.StartsWith("HookEvents.")) {
                    continue;
                }
                foreach (var theEvent in type.Events) {
                    var field = theEvent.DeclaringType.Fields.FirstOrDefault(x => x.Name == theEvent.Name && x.FieldType.FullName == theEvent.EventType.FullName);
                    if (field is null) {
                        continue;
                    }
                    if (field.IsStatic) {
                        raw.ModifiedStaticFields.Add(field);
                    }
                }
            }
        }
    }
}
