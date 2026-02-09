using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class PatcherArguments(
        ModuleDefinition module,
        TypeDefinition rootContextDef,
        Dictionary<string, ContextTypeData> instanceConvdTypeOrigMap,
        Dictionary<string, FieldDefinition> instanceConvdFieldOrigMap,
        Dictionary<string, FieldDefinition> rootContextFieldToAdaptExternalInterface,
        Dictionary<string, TypeDefinition> newConstraintInjectedCtx) : Argument
    {

        public readonly ModuleDefinition MainModule = module;

        public readonly TypeDefinition RootContextDef = rootContextDef;
        /// <summary>
        /// Map from original field full name to instanceConvd field
        /// </summary>
        public readonly FrozenDictionary<string, FieldDefinition> InstanceConvdFieldOrgiMap = instanceConvdFieldOrigMap
            .ToFrozenDictionary();
        /// <summary>
        /// Map from original type full name to instanceConvd type
        /// </summary>
        public readonly FrozenDictionary<string, ContextTypeData> OriginalToContextType = instanceConvdTypeOrigMap
            .ToFrozenDictionary();
        /// <summary>
        /// All instanceConvd fields, sorted by full name
        /// </summary>
        public readonly FrozenDictionary<string, FieldDefinition> InstanceConvdFields = instanceConvdFieldOrigMap.Values
            .ToFrozenDictionary(v => v.GetIdentifier(), v => v);
        /// <summary>
        /// All instanceConvd types, sorted by full name
        /// </summary>
        public readonly FrozenDictionary<string, ContextTypeData> ContextTypes = instanceConvdTypeOrigMap.Values
            .ToFrozenDictionary(v => v.ContextTypeDef.FullName, v => v);
        public readonly FrozenDictionary<string, FieldDefinition> RootContextFieldToAdaptExternalInterface = rootContextFieldToAdaptExternalInterface
            .ToFrozenDictionary();
        public readonly FrozenDictionary<string, TypeDefinition> NewConstraintInjectedCtx = newConstraintInjectedCtx
            .ToFrozenDictionary();
    }
}
