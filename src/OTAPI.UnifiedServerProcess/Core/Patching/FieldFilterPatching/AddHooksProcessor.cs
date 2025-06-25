using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// Just as with <see cref="AddEventsProcessor"/>, binding hooks to the context is always useful. After all, most of them are game logic, and isolation between each server instance is of course necessary.
    /// </summary>
    public class AddHooksProcessor() : IFieldFilterArgProcessor
    {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var type in source.MainModule.GetType("OTAPI.Hooks").NestedTypes) {
                foreach (var field in type.Fields) {
                    source.ModifiedStaticFields.TryAdd(field.GetIdentifier(), field);
                }
            }
        }
    }
}
