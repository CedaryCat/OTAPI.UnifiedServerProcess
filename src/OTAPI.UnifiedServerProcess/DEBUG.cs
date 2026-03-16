
using Mono.Cecil;
using System.Runtime.CompilerServices;

namespace OTAPI.UnifiedServerProcess
{
    public static class DEBUG
    {
        public static void Load(ModuleDefinition module) {
            metadata_importer(module) = new DebugMetadataImporter(module);
        }

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "metadata_importer")]
        static extern ref IMetadataImporter metadata_importer(ModuleDefinition module);

        class DebugMetadataImporter(ModuleDefinition module) : DefaultMetadataImporter(module)
        {
            public override AssemblyNameReference ImportReference(AssemblyNameReference name) {
                return base.ImportReference(name);
            }
        }
    }
}
