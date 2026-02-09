using System.IO;

namespace OTAPI.UnifiedServerProcess
{
    internal class Program
    {
        static void Main(string[] args) {
            DirectoryInfo outputDir = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "output"));
            new PatchExecutor().Patch(outputDir);
        }
    }
}
