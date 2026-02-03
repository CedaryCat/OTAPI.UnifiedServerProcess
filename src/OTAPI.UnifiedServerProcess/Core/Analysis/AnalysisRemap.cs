using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis
{
    public static class AnalysisRemap
    {
        public static void ValidateMethodIdRemap(IReadOnlyDictionary<string, string> oldToNew) {
            ArgumentNullException.ThrowIfNull(oldToNew);

            foreach (var (oldId, newId) in oldToNew) {
                if (string.IsNullOrWhiteSpace(oldId)) {
                    throw new ArgumentException("Method identifier remap contains an empty old identifier.", nameof(oldToNew));
                }
                if (string.IsNullOrWhiteSpace(newId)) {
                    throw new ArgumentException($"Method identifier remap contains an empty new identifier for old '{oldId}'.", nameof(oldToNew));
                }
            }

            var duplicateNew = oldToNew
                .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateNew is not null) {
                var sources = string.Join(", ", duplicateNew.Select(x => $"'{x.Key}'"));
                throw new InvalidOperationException($"Method identifier remap is not 1:1. New identifier '{duplicateNew.Key}' is produced by: {sources}.");
            }
        }

        public static string RemapMethodId(string methodId, IReadOnlyDictionary<string, string> oldToNew) {
            ArgumentNullException.ThrowIfNull(methodId);
            ArgumentNullException.ThrowIfNull(oldToNew);
            return oldToNew.TryGetValue(methodId, out var newId) ? newId : methodId;
        }

        public static void RemapDictionaryKeysInPlace<TValue>(
            Dictionary<string, TValue> dictionary,
            IReadOnlyDictionary<string, string> oldToNew,
            string? dictionaryName = null) {

            ArgumentNullException.ThrowIfNull(dictionary);
            ValidateMethodIdRemap(oldToNew);

            if (oldToNew.Count == 0 || dictionary.Count == 0) {
                return;
            }

            dictionaryName ??= dictionary.GetType().Name;

            var remapped = new Dictionary<string, TValue>(dictionary.Count, StringComparer.Ordinal);
            foreach (var (key, value) in dictionary) {
                var newKey = RemapMethodId(key, oldToNew);
                if (!remapped.TryAdd(newKey, value)) {
                    throw new InvalidOperationException($"Remapping '{dictionaryName}' produced a duplicate key '{newKey}'.");
                }
            }

            dictionary.Clear();
            foreach (var (key, value) in remapped) {
                dictionary.Add(key, value);
            }
        }

        public static ImmutableDictionary<string, TValue> RemapImmutableDictionaryKeys<TValue>(
            ImmutableDictionary<string, TValue> dictionary,
            IReadOnlyDictionary<string, string> oldToNew,
            string? dictionaryName = null) {

            ArgumentNullException.ThrowIfNull(oldToNew);
            ValidateMethodIdRemap(oldToNew);

            if (oldToNew.Count == 0 || dictionary.Count == 0) {
                return dictionary;
            }

            dictionaryName ??= dictionary.GetType().Name;

            var builder = ImmutableDictionary.CreateBuilder<string, TValue>();
            foreach (var (key, value) in dictionary) {
                var newKey = RemapMethodId(key, oldToNew);
                if (builder.ContainsKey(newKey)) {
                    throw new InvalidOperationException($"Remapping '{dictionaryName}' produced a duplicate key '{newKey}'.");
                }
                builder.Add(newKey, value);
            }
            return builder.ToImmutable();
        }

        public static void RemapHashSetInPlace(
            HashSet<string> set,
            IReadOnlyDictionary<string, string> oldToNew,
            string? setName = null) {

            ArgumentNullException.ThrowIfNull(set);
            ValidateMethodIdRemap(oldToNew);

            if (oldToNew.Count == 0 || set.Count == 0) {
                return;
            }

            setName ??= set.GetType().Name;

            var remapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in set) {
                remapped.Add(RemapMethodId(key, oldToNew));
            }

            if (remapped.Count != set.Count) {
                throw new InvalidOperationException($"Remapping '{setName}' produced duplicate entries.");
            }

            set.Clear();
            foreach (var item in remapped) {
                set.Add(item);
            }
        }

        public static bool TryRemapModeMethodStackKey(string key, IReadOnlyDictionary<string, string> oldToNew, out string remappedKey) {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(oldToNew);

            remappedKey = key;

            if (oldToNew.Count == 0) {
                return false;
            }

            var hashIndex = key.IndexOf('#');
            if (hashIndex <= 0) {
                return false;
            }

            var arrowIndex = key.IndexOf('→');
            if (arrowIndex <= hashIndex) {
                return false;
            }

            var mode = key[..hashIndex];
            if (string.Equals(mode, "Field", StringComparison.Ordinal)) {
                return false;
            }

            var methodId = key.Substring(hashIndex + 1, arrowIndex - hashIndex - 1);
            if (!oldToNew.TryGetValue(methodId, out var newMethodId)) {
                return false;
            }

            remappedKey = $"{mode}#{newMethodId}→{key[(arrowIndex + 1)..]}";
            return true;
        }

        public static string RemapModeMethodStackKey(string key, IReadOnlyDictionary<string, string> oldToNew) =>
            TryRemapModeMethodStackKey(key, oldToNew, out var remapped) ? remapped : key;
    }
}

