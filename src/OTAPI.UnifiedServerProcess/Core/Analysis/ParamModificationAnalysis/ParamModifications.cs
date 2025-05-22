using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis
{
    public class ParamModifications(ParameterDefinition parameter)
    {
        public readonly ParameterDefinition parameter = parameter;
        public HashSet<ModifiedComponent> modifications = [];
    }
    public class ModifiedComponent(IEnumerable<MemberAccessStep> accessChain) : IEquatable<ModifiedComponent> {
        public readonly ImmutableArray<MemberAccessStep> modificationAccessChain = [.. accessChain];
        public override string ToString() => string.Join(".", modificationAccessChain);
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
