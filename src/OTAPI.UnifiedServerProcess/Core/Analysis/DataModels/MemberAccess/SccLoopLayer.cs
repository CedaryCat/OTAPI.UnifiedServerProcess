using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess
{
    public sealed class SccLoopLayer(int sccId, TypeReference anchorType) : MemberAccessStep
    {
        public int SccId { get; } = sccId;
        public TypeReference AnchorType { get; } = anchorType;

        public override string Name => $"{{SccLoop:{SccId}}}";
        public override string FullName => Name;
        public override TypeReference DeclaringType => AnchorType;
        public override TypeReference MemberType => AnchorType;

        public override bool IsSameLayer(MemberAccessStep layer) =>
            layer is SccLoopLayer other && other.SccId == SccId;
    }
}

