using System;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class StringExt
    {
        public static bool OrdinalStartsWith(this string source, string value) => source.StartsWith(value, StringComparison.Ordinal);
        public static bool OrdinalStartsWith(this string source, char value) => source.StartsWith(value);
        public static bool OrdinalEndsWith(this string source, string value) => source.EndsWith(value, StringComparison.Ordinal);
        public static bool OrdinalEndsWith(this string source, char value) => source.EndsWith(value);
    }
}
