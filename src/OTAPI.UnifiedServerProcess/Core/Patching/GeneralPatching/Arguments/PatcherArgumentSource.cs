using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class PatcherArgumentSource(ModuleDefinition module, TypeDefinition rootContextDef) : Argument, IArgumentSource<PatcherArgumentSource, PatcherArguments>
    {
        public ModuleDefinition MainModule = module;
        public TypeDefinition RootContextDef = rootContextDef;
        /// <summary>
        /// Map from original field full name to instanceConvd field
        /// </summary>
        public Dictionary<string, FieldDefinition> OriginalToInstanceConvdField = [];
        /// <summary>
        /// Map from original type full name to instanceConvd type
        /// </summary>
        public Dictionary<string, ContextTypeData> OriginalToContextType = [];
        /// <summary>
        /// If a object implement some external interface like IDisposable, we couldn't add root context to its Dispose method, so we should bound a root context in object
        /// <para>Key is type's fullname, value is the root context field</para>
        /// </summary>
        public Dictionary<string, FieldDefinition> RootContextFieldToAdaptExternalInterface = [];

        public Dictionary<string, TypeDefinition> NewConstraintInjectedCtx = [];

        public PatcherArguments Build() =>
            new(MainModule, RootContextDef,
                OriginalToContextType,
                OriginalToInstanceConvdField,
                RootContextFieldToAdaptExternalInterface,
                NewConstraintInjectedCtx);
    }
}
