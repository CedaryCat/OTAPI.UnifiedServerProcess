using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis
{
    public class ParamModifications(ParameterDefinition parameter)
    {
        public readonly ParameterDefinition TrackingParameter = parameter;
        public HashSet<ModifiedComponent> modifications = [];
    }
    public class ModifiedComponent(ParameterDefinition parameter, IEnumerable<MemberAccessStep> accessChain) : IEquatable<ModifiedComponent>
    {
        public readonly ParameterDefinition TrackingParameter = parameter;
        public readonly ImmutableArray<MemberAccessStep> ModificationAccessPath = [.. accessChain];
        public override string ToString() {
            var paramName = TrackingParameter.GetDebugName();
            if (!ModificationAccessPath.IsEmpty) {
                return $"{{ ${paramName}.{string.Join(".", ModificationAccessPath.Select(m => m.Name))} }}";
            }
            else {
                return $"{{ ${paramName} }}";
            }
        }
        public override int GetHashCode() => ToString().GetHashCode();
        public override bool Equals(object? obj) {
            if (obj is ModifiedComponent other) {
                return Equals(other);
            }
            return false;
        }
        public bool Equals(ModifiedComponent? other) {
            return ToString() == other?.ToString();
        }
    }
}
