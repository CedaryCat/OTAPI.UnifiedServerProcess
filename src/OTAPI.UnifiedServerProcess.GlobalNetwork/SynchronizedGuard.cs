namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    public static class SynchronizedGuard
    {
        static readonly Lock cultureFileLock = new Lock();
        static readonly Lock creativeSacrificesLock = new Lock();

        static SynchronizedGuard() {
            On.Terraria.Localization.LanguageManager.LoadFilesForCulture
                += (orig, self, root, culture)
                => { lock (cultureFileLock) { orig(self, root, culture); } };
            On.Terraria.GameContent.Creative.CreativeItemSacrificesCatalog.Initialize
                += (orig, self) => { lock (creativeSacrificesLock) { orig(self); } };
        }

        // Just trigger the static constructor
        public static void Load() { }
    }
}
