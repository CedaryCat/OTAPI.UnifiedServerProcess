using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections.Immutable;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class FilterArgument(ModuleDefinition module, ImmutableArray<FieldDefinition> unmodifiedStaticFields, ImmutableArray<FieldDefinition> modifiedStaticFields) : Argument
    {
        public readonly ModuleDefinition MainModule = module;
        public readonly ImmutableArray<FieldDefinition> UnmodifiedStaticFields = unmodifiedStaticFields;
        public readonly ImmutableArray<FieldDefinition> ModifiedStaticFields = modifiedStaticFields;
    }
}
