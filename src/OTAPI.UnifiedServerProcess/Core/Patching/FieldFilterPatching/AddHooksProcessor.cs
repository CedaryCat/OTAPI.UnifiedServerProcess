using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class AddHooksProcessor : IFieldFilterArgProcessor {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var type in source.MainModule.GetType("OTAPI.Hooks").NestedTypes) {
                foreach (var field in type.Fields) {
                    source.ModifiedStaticFields.Add(field);
                }
            }
        }
    }
}
