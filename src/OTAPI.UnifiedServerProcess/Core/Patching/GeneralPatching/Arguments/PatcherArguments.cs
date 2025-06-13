using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections.Immutable;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class PatcherArguments(
        ModuleDefinition module,
        TypeDefinition rootContextDef,
        ImmutableDictionary<string, ContextTypeData> instanceConvdTypeOrigMap,
        ImmutableDictionary<string, FieldDefinition> instanceConvdFieldOrigMap,
        ImmutableDictionary<string, FieldDefinition> rootContextFieldToAdaptExternalInterface) : Argument
    {

        public readonly ModuleDefinition MainModule = module;

        public readonly TypeDefinition RootContextDef = rootContextDef;
        /// <summary>
        /// Map from original field full name to instanceConvd field
        /// </summary>
        public readonly ImmutableDictionary<string, FieldDefinition> InstanceConvdFieldOrgiMap = instanceConvdFieldOrigMap;
        /// <summary>
        /// Map from original type full name to instanceConvd type
        /// </summary>
        public readonly ImmutableDictionary<string, ContextTypeData> OriginalToContextType = instanceConvdTypeOrigMap;
        /// <summary>
        /// All instanceConvd fields, sorted by full name
        /// </summary>
        public readonly ImmutableDictionary<string, FieldDefinition> InstanceConvdFields = instanceConvdFieldOrigMap.Values
            .ToImmutableDictionary(v => v.FullName, v => v);
        /// <summary>
        /// All instanceConvd types, sorted by full name
        /// </summary>
        public readonly ImmutableDictionary<string, ContextTypeData> ContextTypes = instanceConvdTypeOrigMap.Values
            .ToImmutableDictionary(v => v.ContextTypeDef.FullName, v => v);
        public readonly ImmutableDictionary<string, FieldDefinition> RootContextFieldToAdaptExternalInterface = rootContextFieldToAdaptExternalInterface;
    }
}
