using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// In any case, binding events to the context is always beneficial and harmless. However, OTAPI's HookEvents will contextualize in another way, so skip it.
    /// </summary>
    public class AddEventsProcessor() : IFieldFilterArgProcessor
    {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource raw) {
            foreach (var type in raw.MainModule.GetAllTypes()) {
                if (type.GetRootDeclaringType().Namespace.OrdinalStartsWith("HookEvents.")) {
                    continue;
                }
                foreach (var theEvent in type.Events) {
                    var field = theEvent.DeclaringType.Fields.FirstOrDefault(x => x.Name == theEvent.Name && x.FieldType.FullName == theEvent.EventType.FullName);
                    if (field is null) {
                        continue;
                    }
                    if (field.IsStatic) {
                        raw.ModifiedStaticFields.TryAdd(field.GetIdentifier(), field);
                    }
                }
            }
        }
    }
}
