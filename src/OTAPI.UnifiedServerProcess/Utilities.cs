using System;

namespace OTAPI.UnifiedServerProcess
{
    public static class Utilities
    {

        public static string? GetCliValue(string key) {
            string find = $"-{key}=";
            string? match = Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith(find, StringComparison.CurrentCultureIgnoreCase));
            return match?.Substring(find.Length)?.ToLower();
        }

        public static string? GetGitCommitSha() {
            string? commitSha = Environment.GetEnvironmentVariable("GITHUB_SHA")?.Trim();
            if (commitSha != null && commitSha.Length >= 7) {
                return commitSha.Substring(0, 7);
            }
            return null;
        }
    }
}
