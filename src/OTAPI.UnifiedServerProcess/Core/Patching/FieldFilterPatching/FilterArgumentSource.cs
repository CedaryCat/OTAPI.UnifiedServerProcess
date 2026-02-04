using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{

    public class FilterArgumentSource(ModuleDefinition module, MethodDefinition[] initialMethods) : Argument, IArgumentSource<FilterArgumentSource, FilterArgument>
    {
        public ModuleDefinition MainModule = module;
        public MethodDefinition[] InitialMethods = initialMethods;
        public Dict UnmodifiedStaticFields = [];
        public Dict ModifiedStaticFields = [];
        public Dict InitialStaticFields = [];
        public FilterArgument Build() => new(
            MainModule,
            [.. UnmodifiedStaticFields.Values],
            [.. ModifiedStaticFields.Values],
            [.. InitialStaticFields.Values]);

        public class Dict : Dictionary<string, FieldDefinition>
        {
            public new bool TryAdd(string key, FieldDefinition field) {
                return base.TryAdd(key, field);
            }
            public new bool Remove(string key) {
                return base.Remove(key);
            }
        }
    }
}
