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
            // global share buffer pool
            "Terraria.Net.LegacyNetBufferPool",
            // ignore UIElement._idCounter and UniqueId, it is unused
            "Terraria.UI.UIElement",
        ];
        public static readonly List<string> forceStaticFieldFullNames = [
            // global singleton
            "Terraria.Localization.LocalizedText.Empty",
            // lazy loading cache, should be global
            "Terraria.Localization.LocalizedText._propertyLookupCache", 
        ];
        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {
            foreach (var modified in source.ModifiedStaticFields.ToArray()) {
                // thread static field will not be shared across threads
                if (modified.Value.CustomAttributes.Any(x => x.AttributeType.FullName == "System.ThreadStaticAttribute")) {
                    source.ModifiedStaticFields.Remove(modified.Key);
                }
                if (forceStaticTypeFullNames.Contains(modified.Value.DeclaringType.FullName)) {
                    source.ModifiedStaticFields.Remove(modified.Key);
                }
            }
            foreach (var field in forceStaticFieldFullNames) {
                source.ModifiedStaticFields.Remove(field);
            }
        }
    }
}
