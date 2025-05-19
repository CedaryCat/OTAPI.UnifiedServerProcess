namespace OTAPI.UnifiedServerProcess {
    internal class Program {
        static void Main(string[] args) {
            new PatchExecutor().Patch();
        }
    }
}
