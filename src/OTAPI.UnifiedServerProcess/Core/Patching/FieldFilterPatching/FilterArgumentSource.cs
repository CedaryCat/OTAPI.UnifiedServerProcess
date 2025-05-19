using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching {
    public class FilterArgumentSource(ModuleDefinition module, string[] unmodifiedStaticFieldFullNames) : Argument, IArgumentSource<FilterArgumentSource, FilterArgument> {
        public ModuleDefinition MainModule = module;
        public string[] UnmodifiedStaticFieldFullNames = unmodifiedStaticFieldFullNames;
        public HashSet<FieldDefinition> UnmodifiedStaticFields = [];
        public HashSet<FieldDefinition> ModifiedStaticFields = [];
        public FilterArgument Build() => new(MainModule, [.. UnmodifiedStaticFields], [.. ModifiedStaticFields]);
    }
}
