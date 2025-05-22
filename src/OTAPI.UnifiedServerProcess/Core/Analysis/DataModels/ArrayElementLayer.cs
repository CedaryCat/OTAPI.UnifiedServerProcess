using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels {
    public sealed class ArrayElementLayer(ArrayType declaringType) : MemberAccessStep {
        private readonly ArrayType arrayType = declaringType;

        public sealed override string Name => "{Index}";
        public sealed override string FullName => arrayType.FullName + "." + Name;
        public sealed override TypeReference DeclaringType => arrayType;
        public sealed override TypeReference MemberType => arrayType.ElementType;
    }
}
