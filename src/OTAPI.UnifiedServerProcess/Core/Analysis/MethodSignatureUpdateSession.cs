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
        private readonly Dictionary<MethodDefinition, (string OldType, string NewType)> _returnTypeChanges = [];

        public IReadOnlyDictionary<MethodDefinition, IReadOnlyDictionary<int, (string OldType, string NewType)>> PlannedParameterTypeChanges =>
            _paramTypeChanges.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, (string, string)>)kv.Value);

        public IReadOnlyDictionary<MethodDefinition, (string OldType, string NewType)> PlannedReturnTypeChanges => _returnTypeChanges;

        public void PlanParameterTypeChange(MethodDefinition method, int parameterIndex, TypeReference newParameterType) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(newParameterType);

            if (parameterIndex < 0 || parameterIndex >= method.Parameters.Count) {
                throw new ArgumentOutOfRangeException(nameof(parameterIndex), $"Parameter index {parameterIndex} is out of range for method '{method.GetIdentifier()}'.");
            }

            string oldType = method.Parameters[parameterIndex].ParameterType.FullName;
            string newType = newParameterType.FullName;

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

        public void PlanReturnTypeChange(MethodDefinition method, TypeReference newReturnType) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(newReturnType);

            string oldType = method.ReturnType.FullName;
            string newType = newReturnType.FullName;

            if (string.Equals(oldType, newType, StringComparison.Ordinal)) {
                return;
            }

            if (!_before.ContainsKey(method)) {
                _before.Add(method, MethodSnapshot.Capture(method));
            }

            if (_returnTypeChanges.TryGetValue(method, out var existing)) {
                if (string.Equals(oldType, existing.NewType, StringComparison.Ordinal)
                    && string.Equals(newType, existing.NewType, StringComparison.Ordinal)) {
                    return;
                }
                if (!string.Equals(existing.OldType, oldType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Method '{method.GetIdentifier()}' return old type mismatch. Expected '{existing.OldType}', found '{oldType}'.");
                }
                if (!string.Equals(existing.NewType, newType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Method '{method.GetIdentifier()}' return new type conflict. '{existing.NewType}' vs '{newType}'.");
                }
                return;
            }

            _returnTypeChanges.Add(method, (oldType, newType));
        }

        public Dictionary<string, string> BuildOldToNewMethodIdMapAndValidate() {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var newIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (method, beforeSnapshot) in _before) {
                var afterSnapshot = MethodSnapshot.Capture(method);

                ValidateCompatible(beforeSnapshot, afterSnapshot);

                bool hasPlannedParamChanges = _paramTypeChanges.TryGetValue(method, out var expectedParamChanges) && expectedParamChanges.Count > 0;
                bool hasPlannedReturnChange = _returnTypeChanges.ContainsKey(method);

                if (!hasPlannedParamChanges && !hasPlannedReturnChange) {
                    throw new InvalidOperationException($"Method '{beforeSnapshot.Identifier}' is in the session but has no planned signature changes.");
                }

                ValidateOnlyExpectedParameterTypeChanges(
                    beforeSnapshot,
                    afterSnapshot,
                    expectedParamChanges ?? new Dictionary<int, (string OldType, string NewType)>()
                );

                (string OldType, string NewType)? expectedReturnChange = null;
                if (_returnTypeChanges.TryGetValue(method, out var expectedReturn)) {
                    expectedReturnChange = expectedReturn;
                }
                ValidateOnlyExpectedReturnTypeChanges(beforeSnapshot, afterSnapshot, expectedReturnChange);

                string oldId = beforeSnapshot.Identifier;
                string newId = afterSnapshot.Identifier;

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
            if (before.ParameterCount != after.ParameterCount) {
                throw new InvalidOperationException($"Parameter count changed for '{before.Identifier}': {before.ParameterCount} -> {after.ParameterCount}.");
            }
        }

        private static void ValidateOnlyExpectedParameterTypeChanges(
            MethodSnapshot before,
            MethodSnapshot after,
            Dictionary<int, (string OldType, string NewType)> expectedChanges) {

            for (int i = 0; i < before.ParameterTypeFullNames.Length; i++) {
                string oldType = before.ParameterTypeFullNames[i];
                string newType = after.ParameterTypeFullNames[i];

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

        private static void ValidateOnlyExpectedReturnTypeChanges(
            MethodSnapshot before,
            MethodSnapshot after,
            (string OldType, string NewType)? expectedChange) {

            string oldType = before.ReturnTypeFullName;
            string newType = after.ReturnTypeFullName;

            if (expectedChange is { } expected) {
                if (!string.Equals(expected.OldType, oldType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Planned old return type mismatch for '{before.Identifier}'. Expected '{expected.OldType}', found '{oldType}'.");
                }
                if (!string.Equals(expected.NewType, newType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Planned new return type mismatch for '{before.Identifier}'. Expected '{expected.NewType}', found '{newType}'.");
                }
            }
            else {
                if (!string.Equals(oldType, newType, StringComparison.Ordinal)) {
                    throw new InvalidOperationException($"Unexpected return type change for '{before.Identifier}': '{oldType}' -> '{newType}'.");
                }
            }
        }
    }
}
