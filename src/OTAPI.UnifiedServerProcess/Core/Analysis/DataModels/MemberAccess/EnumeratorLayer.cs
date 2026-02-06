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
        static TypeReference GetEnumeratorType(ModuleDefinition module) {
            return enumeratorType ??= module.ImportReference(typeof(IEnumerator<>));
        }
        static TypeReference GetEnumerableType(ModuleDefinition module) {
            return enumerableType ??= module.ImportReference(typeof(IEnumerable<>));
        }
        public static bool IsEnumerator(TypeInheritanceGraph graph, TypeDefinition type) {
            var inheritance = graph.GetInheritanceTypes(type);
            if (inheritance.Count == 0) {
                return false;
            }
            if (!inheritance.ContainsKey(GetEnumerableType(type.Module).FullName)) {
                return false;
            }
            return true;
        }
        public static bool IsGetEnumeratorMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction getEnumeratorInstruction) {
            if (getEnumeratorInstruction.OpCode != OpCodes.Call || getEnumeratorInstruction.OpCode != OpCodes.Callvirt) {
                return false;
            }
            if (getEnumeratorInstruction.Operand is not MethodReference methodRef) {
                return false;
            }
            if (methodRef.Name != "GetEnumerator") {
                return false;
            }
            var declaringType = methodRef.DeclaringType.TryResolve();
            if (declaringType is null) {
                return false;
            }
            var inheritance = graph.GetInheritanceTypes(declaringType);
            if (inheritance.Count == 0) {
                return false;
            }
            if (!inheritance.ContainsKey(GetEnumerableType(caller.Module).FullName)) {
                return false;
            }
            return true;
        }
        public static bool IsGetCurrentMethod(TypeInheritanceGraph graph, MethodReference caller, Instruction getEnumeratorInstruction) {
            if (getEnumeratorInstruction.OpCode != OpCodes.Call && getEnumeratorInstruction.OpCode != OpCodes.Callvirt) {
                return false;
            }
            if (getEnumeratorInstruction.Operand is not MethodReference methodRef) {
                return false;
            }
            if (methodRef.Name != "get_Current") {
                return false;
            }
            var declaringType = methodRef.DeclaringType.TryResolve();
            if (declaringType is null) {
                return false;
            }
            var inheritance = graph.GetInheritanceTypes(declaringType);
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
