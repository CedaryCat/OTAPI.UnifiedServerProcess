using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess
{
    public class EnumeratorLayer(TypeReference collectionType) : MemberAccessStep
    {
        public override string Name => "{InnerEnumerable}";
        public override string FullName => Name;
        public override TypeReference DeclaringType => GetEnumeratorType(collectionType.Module);
        public override TypeReference MemberType => collectionType;
        static TypeReference? enumeratorType;
        static TypeReference? enumerableType;
        public static TypeReference GetEnumeratorType(ModuleDefinition module) {
            return enumeratorType ??= module.ImportReference(typeof(IEnumerator<>));
        }
        public static TypeReference GetEnumerableType(ModuleDefinition module) {
            return enumerableType ??= module.ImportReference(typeof(IEnumerable<>));
        }
        public static bool IsEnumerator(TypeInheritanceGraph graph, TypeDefinition type) {
            Dictionary<string, TypeDefinition> inheritance = graph.GetInheritanceTypes(type);
            if (inheritance.Count == 0) {
                return false;
            }
            if (!inheritance.ContainsKey(GetEnumerableType(type.Module).FullName)) {
                return false;
            }
            return true;
        }
        public static bool IsGetEnumeratorMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction getEnumeratorInstruction) {
            if (getEnumeratorInstruction is not {
                OpCode.Code: Code.Call or Code.Callvirt,
                Operand: MethodReference { HasThis: true } getEnumerator
            }) {
                return false;
            }
            if (getEnumerator.Name != "GetEnumerator") {
                return false;
            }
            TypeDefinition? declaringType = getEnumerator.DeclaringType.TryResolve();
            if (declaringType is null) {
                return false;
            }
            Dictionary<string, TypeDefinition> inheritance = graph.GetInheritanceTypes(declaringType);
            if (inheritance.Count == 0) {
                return false;
            }
            if (!inheritance.ContainsKey(GetEnumerableType(caller.Module).FullName)) {
                return false;
            }
            return true;
        }
        public static bool IsGetCurrentMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction getCurrentInstruction) {
            if (getCurrentInstruction is not {
                OpCode.Code: Code.Call or Code.Callvirt,
                Operand: MethodReference { HasThis: true } getCurrent
            }) {
                return false;
            }
            if (getCurrent.Name != "get_Current") {
                return false;
            }
            TypeDefinition? declaringType = getCurrent.DeclaringType.TryResolve();
            if (declaringType is null) {
                return false;
            }
            Dictionary<string, TypeDefinition> inheritance = graph.GetInheritanceTypes(declaringType);
            if (inheritance.Count == 0) {
                return false;
            }
            if (!inheritance.ContainsKey(GetEnumeratorType(caller.Module).FullName)) {
                return false;
            }
            return true;
        }
    }
}
