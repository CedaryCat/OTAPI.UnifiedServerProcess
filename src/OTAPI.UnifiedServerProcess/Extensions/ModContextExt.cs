using ModFramework;
using System.IO;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class ModContextExt
    {
        public static string ExtractResources(this ModContext modcontext, string fileinput) {
            string dir = GetEmbeddedResourcesDirectory(modcontext, fileinput);
            var extractor = new ResourceExtractor();
            string embeddedResourcesDir = extractor.Extract(fileinput, dir);
            return embeddedResourcesDir;
        }
        public static string GetEmbeddedResourcesDirectory(ModContext modcontext, string fileinput) {
            return Path.Combine(modcontext.BaseDirectory, Path.GetDirectoryName(fileinput)!);
        }
    }
}
