using Mono.Cecil;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels {
    public abstract class MemberAccessStep : IEquatable<MemberAccessStep> {
        public abstract string Name { get; }
        public abstract string FullName { get; }
        public abstract TypeReference DeclaringType { get; }
        public abstract TypeReference MemberType { get; }
        public static implicit operator MemberAccessStep(MemberReference member) => new RealMemberLayer(member);
        public virtual bool IsSameLayer(MemberAccessStep layer) {
            return layer.FullName == FullName;
        }

        public bool Equals(MemberAccessStep? other) {
            return FullName == other?.FullName;
        }
        public override bool Equals(object? obj) {
            return obj is MemberAccessStep other && Equals(other);
        }
        public override int GetHashCode() {
            return FullName.GetHashCode();
        }
    }
}
