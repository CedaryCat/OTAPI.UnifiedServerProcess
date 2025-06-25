using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess
{
    public sealed class ArrayElementLayer(ArrayType declaringType) : CollectionElementLayer(declaringType, declaringType.ElementType)
    {
        private readonly ArrayType arrayType = declaringType;
    }
}
