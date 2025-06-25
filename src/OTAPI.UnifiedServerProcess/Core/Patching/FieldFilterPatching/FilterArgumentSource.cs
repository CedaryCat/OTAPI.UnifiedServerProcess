using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    
    public class FilterArgumentSource(ModuleDefinition module, MethodDefinition[] initialMethods) : Argument, IArgumentSource<FilterArgumentSource, FilterArgument>
    {
        public ModuleDefinition MainModule = module;
        public MethodDefinition[] InitialMethods = initialMethods;
        public Dictionary<string, FieldDefinition> UnmodifiedStaticFields = [];
        public Dictionary<string, FieldDefinition> ModifiedStaticFields = [];
        public Dictionary<string, FieldDefinition> InitialStaticFields = [];
        public FilterArgument Build() => new(
            MainModule, 
            [.. UnmodifiedStaticFields.Values], 
            [.. ModifiedStaticFields.Values],
            [.. InitialStaticFields.Values]);
    }
}
