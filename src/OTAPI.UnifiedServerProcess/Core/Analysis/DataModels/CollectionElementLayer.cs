using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels {
    public class CollectionElementLayer(TypeReference collectionType, TypeReference elementType) : MemberLayer {
        public sealed override string Name => "{Element}";
        public sealed override string FullName => collectionType.FullName + "." + Name;
        public sealed override TypeReference DeclaringType => collectionType;
        public sealed override TypeReference MemberType => elementType;

        static TypeDefinition? ICollectionType;
        static TypeDefinition? ISetType;
        static TypeDefinition? IListType;
        static TypeDefinition? IDictionaryType;
        static TypeDefinition? QueueType;
        static TypeDefinition? StackType;

        [MemberNotNull(nameof(ICollectionType), nameof(ISetType), nameof(IListType), nameof(IDictionaryType), nameof(QueueType), nameof(StackType))]
        static void LazyInit(ModuleDefinition module) {
            ICollectionType ??= module.ImportReference(typeof(ICollection<>)).Resolve();
            ISetType ??= module.ImportReference(typeof(ISet<>)).Resolve();
            IListType ??= module.ImportReference(typeof(IList<>)).Resolve();
            IDictionaryType ??= module.ImportReference(typeof(IDictionary<,>)).Resolve();
            QueueType ??= module.ImportReference(typeof(Queue<>)).Resolve();
            StackType ??= module.ImportReference(typeof(Stack<>)).Resolve();
        }

        public static bool IsStoreElementMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction storeMethodCallInstruction, out int indexOfValueInParameters) {

            indexOfValueInParameters = -1;
            if (storeMethodCallInstruction.Operand is not MethodReference storeMethod) {
                return false;
            }

            var resolvedStoreMethod = storeMethod.Resolve();

            if (resolvedStoreMethod.Name is "set_Item" && resolvedStoreMethod.IsSpecialName) {
                // 0,1,...,last - 1: index or key,
                // last: value
                indexOfValueInParameters = resolvedStoreMethod.Parameters.Count - 1;
                return true;
            }

            LazyInit(caller.Module);
            var inheritancesTypes = graph.GetInheritancesTypes(resolvedStoreMethod.DeclaringType);

            if (inheritancesTypes.ContainsKey(IDictionaryType.FullName) && (resolvedStoreMethod.Name is "Add" || resolvedStoreMethod.Name is "TryAdd")) {
                // 0: key, 1: value
                indexOfValueInParameters = 1;
                return true;
            }

            if (inheritancesTypes.ContainsKey(ICollectionType.FullName) && resolvedStoreMethod.Name is "Add") {
                // 0: value
                indexOfValueInParameters = 0;
                return true;

            }

            if (inheritancesTypes.ContainsKey(ISetType.FullName) && resolvedStoreMethod.Name is "Add") {
                // 0: value
                indexOfValueInParameters = 0;
                return true;
            }

            if (inheritancesTypes.ContainsKey(IListType.FullName) && (resolvedStoreMethod.Name is "Add" || resolvedStoreMethod.Name is "Insert")) {
                // 0: value
                indexOfValueInParameters = 0;
                return true;
            }

            if (inheritancesTypes.ContainsKey(QueueType.FullName) && resolvedStoreMethod.Name is "Enqueue") {
                // 0: value
                indexOfValueInParameters = 0;
                return true;
            }

            if (inheritancesTypes.ContainsKey(StackType.FullName) && resolvedStoreMethod.Name is "Push") {
                // 0: value
                indexOfValueInParameters = 0;
                return true;
            }

            return false;
        }

        public static bool IsLoadElementMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction loadMethodCallInstruction, out int indexOfOutValueInParameters) {
            indexOfOutValueInParameters = -1;
            if (loadMethodCallInstruction.Operand is not MethodReference loadMethod) {
                return false;
            }

            var resolvedLoadMethod = loadMethod.Resolve();

            if (resolvedLoadMethod.Name is "get_Item" && resolvedLoadMethod.IsSpecialName) {
                return true;
            }

            LazyInit(caller.Module);
            var inheritancesTypes = graph.GetInheritancesTypes(resolvedLoadMethod.DeclaringType);

            if (inheritancesTypes.ContainsKey(IDictionaryType.FullName) && resolvedLoadMethod.Name is "TryGetValue") {
                // 0: key, 1: out value
                indexOfOutValueInParameters = 1;
                return true;
            }

            if (inheritancesTypes.ContainsKey(QueueType.FullName)) {
                if (resolvedLoadMethod.Name is "Peek" or "Dequeue") {
                    indexOfOutValueInParameters = 0;
                    return true;
                }
                if (resolvedLoadMethod.Name is "TryPeek" or "TryDequeue") {
                    indexOfOutValueInParameters = 1;
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(StackType.FullName)) {
                if (resolvedLoadMethod.Name is "Peek" or "Pop") {
                    indexOfOutValueInParameters = 0;
                }
                if (resolvedLoadMethod.Name is "TryPeek" or "TryPop") {
                    indexOfOutValueInParameters = 1;
                }
                return true;
            }

            return false;
        }
        public static bool IsModificationMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction modifyMethodCallInstruction) {
            if (modifyMethodCallInstruction.Operand is not MethodReference modifyMethod) {
                return false;
            }

            var resolvedModifyMethod = modifyMethod.Resolve();
            LazyInit(caller.Module);

            var inheritancesTypes = graph.GetInheritancesTypes(resolvedModifyMethod.DeclaringType);

            if (resolvedModifyMethod.Name is "set_Item" && resolvedModifyMethod.IsSpecialName) {
                return true;
            }

            if (inheritancesTypes.ContainsKey(ICollectionType.FullName)) {
                if (resolvedModifyMethod.Name is "Add" or "Remove" or "Clear") {
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(ISetType.FullName)) {
                if (resolvedModifyMethod.Name is "Add" or "Clear" or "ExceptWith" or "IntersectWith" or "SymmetricExceptWith" or "UnionWith") {
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(IListType.FullName)) {
                if (resolvedModifyMethod.Name is "Insert" or "RemoveAt") {
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(IDictionaryType.FullName)) {
                if (resolvedModifyMethod.Name is "Add" or "Remove" or "Clear") {
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(QueueType.FullName)) {
                if (resolvedModifyMethod.Name is "Enqueue" or "Dequeue" or "TryDequeue" or "Clear") {
                    return true;
                }
            }

            if (inheritancesTypes.ContainsKey(StackType.FullName)) {
                if (resolvedModifyMethod.Name is "Push" or "Pop" or "TryPop" or "Clear") {
                    return true;
                }
            }

            return false;
        }
        // TODO: support other collections and LINQ
    }
}
