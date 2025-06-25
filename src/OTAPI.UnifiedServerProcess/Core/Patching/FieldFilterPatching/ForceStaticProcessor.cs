using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    /// <summary>
    /// There are some rather special logics. It's best to maintain global static, otherwise, it's easy for some regular functions to be unexpectedly localized.
    /// <para>such as NetworkText.ToString()</para>
    /// </summary>
    public class ForceStaticProcessor() : IFieldFilterArgProcessor
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
            // ignore hit tile, it should not run on server
            "Terraria.HitTile",

            "Terraria.ObjectData.TileObjectData._baseObject"
        ];
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var modified in source.ModifiedStaticFields.ToArray()) {
                if (forceStaticTypeFullNames.Contains(modified.Value.DeclaringType.FullName)) {
                    source.ModifiedStaticFields.Remove(modified.Key);
                }
            }
        }
    }
}
