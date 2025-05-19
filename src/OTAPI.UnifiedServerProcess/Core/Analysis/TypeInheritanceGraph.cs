using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace OTAPI.UnifiedServerProcess.Core.Analysis {
    public class TypeInheritanceGraph {
        public TypeInheritanceGraph(ModuleDefinition module) {
            TypeInheritanceChains = [];
            foreach (var type in GetTypesInInheritanceOrder(module)) {
                GetInheritancesTypes(type);
            }
        }
        public ImmutableDictionary<string, TypeDefinition> GetInheritancesTypes(TypeDefinition type) {
            if (TypeInheritanceChains.TryGetValue(type.FullName, out var result)) {
                return result;
            }
            var types = new Dictionary<string, TypeDefinition>();
            var currentType = type;
            while (currentType != null) {
                types.TryAdd(currentType.FullName, currentType);

                foreach (var interfaceType in currentType.Interfaces) {

                    var resolvedInterfaceType = interfaceType.InterfaceType.TryResolve();

                    if (resolvedInterfaceType is null) continue;

                    types.TryAdd(resolvedInterfaceType.FullName, resolvedInterfaceType);
                }
                currentType = currentType.BaseType?.TryResolve();
            }
            result = types.ToImmutableDictionary();
            TypeInheritanceChains.Add(type.FullName, result);
            return result;
        }

        readonly Dictionary<string, ImmutableDictionary<string, TypeDefinition>> TypeInheritanceChains; 
        private static List<TypeDefinition> GetTypesInInheritanceOrder(ModuleDefinition module) {
            var allTypes = new HashSet<TypeDefinition>();
            var sorted = new List<TypeDefinition>();

            foreach (var type in module.GetTypes()) {
                VisitType(type, allTypes, sorted);
            }

            return sorted;
        }

        private static void VisitType(
            TypeDefinition type,
            HashSet<TypeDefinition> visited,
            List<TypeDefinition> sorted) {
            if (type is null || visited.Contains(type)) return;

            var baseType = type.BaseType?.TryResolve();
            if (baseType != null && !visited.Contains(baseType)) {
                VisitType(baseType, visited, sorted);
            }

            foreach (var iface in type.Interfaces) {
                var interfaceType = iface.InterfaceType.TryResolve();
                if (interfaceType is null) {
                    continue;
                }
                VisitType(interfaceType, visited, sorted);
            }

            if (visited.Add(type)) {
                sorted.Add(type);
            }
        }
    }
}
