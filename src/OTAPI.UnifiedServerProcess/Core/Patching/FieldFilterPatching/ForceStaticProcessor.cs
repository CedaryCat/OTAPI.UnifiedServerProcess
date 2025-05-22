using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class ForceStaticProcessor : IFieldFilterArgProcessor
    {
        public static readonly HashSet<string> forceStaticTypeFullNames = [
            // ignore platform
            "ReLogic.OS.Platform",
            // ignore assets, it should not run on server
            "Terraria.GameContent.TextureAssets",
            "Terraria.GameContent.TextureAssets/RenderTargets",
            "Terraria.GameContent.FontAssets",
            // ignore social api, it should not run on server
            "Terraria.Social.SocialAPI",
            // ignore language, it should be a global singleton
            "Terraria.Lang",
            "Terraria.Localization.Language",
            "Terraria.Localization.LanguageManager",
            "Terraria.Localization.GameCulture",
        ];
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var modified in source.ModifiedStaticFields.ToArray()) {
                if (forceStaticTypeFullNames.Contains(modified.DeclaringType.FullName)) {
                    source.ModifiedStaticFields.Remove(modified);
                }
            }
        }
    }
}
