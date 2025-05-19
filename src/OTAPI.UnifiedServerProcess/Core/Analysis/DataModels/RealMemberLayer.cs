using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels {
    public sealed class RealMemberLayer(MemberReference member) : MemberLayer {
        public readonly MemberReference Member = member;
        public sealed override string Name => Member.Name;
        public sealed override string FullName => Member.FullName;
        public sealed override TypeReference DeclaringType => Member.DeclaringType;
        public sealed override TypeReference MemberType => ((FieldReference)Member).FieldType;
    }
}
