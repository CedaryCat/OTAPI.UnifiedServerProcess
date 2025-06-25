using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess
{
    public sealed class RealMemberLayer(MemberReference member) : MemberAccessStep
    {
        public readonly MemberReference Member = member;
        public sealed override string Name => Member.Name;
        public sealed override string FullName => Member.FullName;
        public sealed override TypeReference DeclaringType => Member.DeclaringType;
        public sealed override TypeReference MemberType => ((FieldReference)Member).FieldType;
    }
}
