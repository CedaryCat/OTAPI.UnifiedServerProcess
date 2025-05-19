using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class IgnoreLanguageProcessor : IFieldFilterArgProcessor {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var modified in source.ModifiedStaticFields.ToArray()) {
                if (modified.DeclaringType.FullName == "Terraria.Localization.Language" 
                    || modified.DeclaringType.FullName == "Terraria.Localization.GameCulture") {
                    source.ModifiedStaticFields.Remove(modified);
                }
            }
        }
    }
}
