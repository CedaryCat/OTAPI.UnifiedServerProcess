using System;
using System.IO;
using System.Reflection;

namespace OTAPI.UnifiedServerProcess
{
    internal class Program
    {
        static void Main(string[] args) {
            var outputDir = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "output"));
            new PatchExecutor().Patch(outputDir);
        }
    }
}
