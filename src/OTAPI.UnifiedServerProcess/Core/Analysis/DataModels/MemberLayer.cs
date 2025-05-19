using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels {
    public abstract class MemberLayer {
        public abstract string Name { get; }
        public abstract string FullName { get; }
        public abstract TypeReference DeclaringType { get; }
        public abstract TypeReference MemberType { get; }
        public static implicit operator MemberLayer(MemberReference member) => new RealMemberLayer(member);
        public virtual bool IsSameLayer(MemberLayer layer) {
            return layer.FullName == FullName;
        }
    }
}
