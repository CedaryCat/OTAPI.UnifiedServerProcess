using System.Reflection;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork
{
    public class VersionHelper
    {
        public readonly string TerrariaVersion;
        public readonly string OTAPIVersion;

        public VersionHelper() {
            var otapi = typeof(Terraria.Main).Assembly;
            var fileVersionAttr = otapi.GetCustomAttribute<AssemblyFileVersionAttribute>();
            TerrariaVersion = fileVersionAttr!.Version;

            var informationalVersionAttr = otapi.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            OTAPIVersion = informationalVersionAttr!.InformationalVersion.Split('+').First();
        }
    }
}
