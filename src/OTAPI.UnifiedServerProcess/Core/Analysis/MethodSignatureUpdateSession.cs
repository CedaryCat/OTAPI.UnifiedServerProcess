using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis
{
    public sealed class MethodSignatureUpdateSession
    {
        private sealed record MethodSnapshot(
            string Identifier,
            string DeclaringTypeFullName,
            string Name,
            int GenericParameterCount,
            string ReturnTypeFullName,
            ImmutableArray<string> ParameterTypeFullNames)
        {
            public int ParameterCount => ParameterTypeFullNames.Length;

            public static MethodSnapshot Capture(MethodDefinition method) {
                ArgumentNullException.ThrowIfNull(method);
                if (method.DeclaringType is null) {
                    throw new ArgumentException("Method.DeclaringType is null.", nameof(method));
                }

                return new MethodSnapshot(
                    Identifier: method.GetIdentifier(),
                    DeclaringTypeFullName: method.DeclaringType.FullName,
                    Name: method.Name,
                    GenericParameterCount: method.GenericParameters.Count,
                    ReturnTypeFullName: method.ReturnType.FullName,
                    ParameterTypeFullNames: [.. method.Parameters.Select(p => p.ParameterType.FullName)]
                );
            }
        }

        private readonly Dictionary<MethodDefinition, MethodSnapshot> _before = [];
        private readonly Dictionary<MethodDefinition, Dictionary<int, (string OldType, string NewType)>> _paramTypeChanges = [];

        public IReadOnlyDictionary<MethodDefinition, IReadOnlyDictionary<int, (string OldType, string NewType)>> PlannedParameterTypeChanges =>
            _paramTypeChanges.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, (string, string)>)kv.Value);

        public void PlanParameterTypeChange(MethodDefinition method, int parameterIndex, TypeReference newParameterType) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(newParameterType);

            if (parameterIndex < 0 || parameterIndex >= method.Parameters.Count) {
                throw new ArgumentOutOfRangeException(nameof(parameterIndex), $"Parameter index {parameterIndex} is out of range for method '{method.GetIdentifier()}'.");
            }

            var oldType = method.Parameters[parameterIndex].ParameterType.FullName;
            var newType = newParameterType.FullName;

            if (!_before.ContainsKey(method)) {
                _before.Add(method, MethodSnapshot.Capture(method));
            }

            if (!_paramTypeChanges.TryGetValue(method, out var perMethod)) {
                perMethod = [];
                _paramTypeChanges.Add(method, perMethod);
            }

            if (perMethod.TryGetValue(parameterIndex, out var existing)) {
                if (!string.Equals(existing.OldType, oldType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Method '{method.GetIdentifier()}' parameter[{parameterIndex}] old type mismatch. Expected '{existing.OldType}', found '{oldType}'.");
                }
                if (!string.Equals(existing.NewType, newType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Method '{method.GetIdentifier()}' parameter[{parameterIndex}] new type conflict. '{existing.NewType}' vs '{newType}'.");
                }
                return;
            }

            perMethod.Add(parameterIndex, (oldType, newType));
        }

        public Dictionary<string, string> BuildOldToNewMethodIdMapAndValidate() {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var newIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (method, beforeSnapshot) in _before) {
                var afterSnapshot = MethodSnapshot.Capture(method);

                ValidateCompatible(beforeSnapshot, afterSnapshot);

                if (!_paramTypeChanges.TryGetValue(method, out var expectedChanges) || expectedChanges.Count == 0) {
                    throw new InvalidOperationException($"Method '{beforeSnapshot.Identifier}' is in the session but has no planned parameter type changes.");
                }

                ValidateOnlyExpectedParameterTypeChanges(beforeSnapshot, afterSnapshot, expectedChanges);

                var oldId = beforeSnapshot.Identifier;
                var newId = afterSnapshot.Identifier;

                if (string.Equals(oldId, newId, StringComparison.Ordinal)) {
                    continue;
                }

                if (!result.TryAdd(oldId, newId)) {
                    throw new InvalidOperationException($"Duplicate old method identifier '{oldId}' in remap session.");
                }
                if (!newIds.Add(newId)) {
                    throw new InvalidOperationException($"Duplicate new method identifier '{newId}' in remap session.");
                }
            }

            return result;
        }

        private static void ValidateCompatible(MethodSnapshot before, MethodSnapshot after) {
            if (!string.Equals(before.DeclaringTypeFullName, after.DeclaringTypeFullName, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Declaring type changed: '{before.DeclaringTypeFullName}' -> '{after.DeclaringTypeFullName}'.");
            }
            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Method name changed: '{before.Name}' -> '{after.Name}'.");
            }
            if (before.GenericParameterCount != after.GenericParameterCount) {
                throw new InvalidOperationException($"Generic parameter count changed for '{before.Identifier}': {before.GenericParameterCount} -> {after.GenericParameterCount}.");
            }
            if (!string.Equals(before.ReturnTypeFullName, after.ReturnTypeFullName, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Return type changed for '{before.Identifier}': '{before.ReturnTypeFullName}' -> '{after.ReturnTypeFullName}'.");
            }
            if (before.ParameterCount != after.ParameterCount) {
                throw new InvalidOperationException($"Parameter count changed for '{before.Identifier}': {before.ParameterCount} -> {after.ParameterCount}.");
            }
        }

        private static void ValidateOnlyExpectedParameterTypeChanges(
            MethodSnapshot before,
            MethodSnapshot after,
            Dictionary<int, (string OldType, string NewType)> expectedChanges) {

            for (int i = 0; i < before.ParameterTypeFullNames.Length; i++) {
                var oldType = before.ParameterTypeFullNames[i];
                var newType = after.ParameterTypeFullNames[i];

                if (expectedChanges.TryGetValue(i, out var expected)) {
                    if (!string.Equals(expected.OldType, oldType, StringComparison.Ordinal)) {
                        throw new InvalidOperationException($"Planned old type mismatch for '{before.Identifier}' param[{i}]. Expected '{expected.OldType}', found '{oldType}'.");
                    }
                    if (!string.Equals(expected.NewType, newType, StringComparison.Ordinal)) {
                        throw new InvalidOperationException($"Planned new type mismatch for '{before.Identifier}' param[{i}]. Expected '{expected.NewType}', found '{newType}'.");
                    }
                }
                else {
                    if (!string.Equals(oldType, newType, StringComparison.Ordinal)) {
                        throw new InvalidOperationException($"Unexpected parameter type change for '{before.Identifier}' param[{i}]: '{oldType}' -> '{newType}'.");
                    }
                }
            }
        }
    }
}

