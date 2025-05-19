using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class IgnoreAssetsProccessor : IFieldFilterArgProcessor {
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var modified in source.ModifiedStaticFields.ToArray()) {
                if (modified.DeclaringType.FullName == "Terraria.GameContent.TextureAssets"
                    || modified.DeclaringType.FullName == "Terraria.GameContent.TextureAssets/RenderTargets"
                    || modified.DeclaringType.FullName == "Terraria.GameContent.FontAssets") {
                    source.ModifiedStaticFields.Remove(modified);
                }
            }
        }
    }
}
