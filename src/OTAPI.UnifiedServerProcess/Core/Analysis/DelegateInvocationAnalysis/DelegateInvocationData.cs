using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis {
    public class DelegateInvocationData {
        public DelegateInvocationData(MethodDefinition method, Instruction pushDelegateInstruction) {
            Method = method;
            PushDelegateInstruction = pushDelegateInstruction;
            Key = GenerateStackKey(method, pushDelegateInstruction);
            combinedFromMap.Add(invocations, [this]);
        }
        public readonly MethodDefinition Method;
        public readonly Instruction PushDelegateInstruction;
        public readonly string Key;
        private Dictionary<string, MethodDefinition> invocations = [];

        static Dictionary<Dictionary<string, MethodDefinition>, HashSet<DelegateInvocationData>> combinedFromMap = [];

        public IReadOnlyDictionary<string, MethodDefinition> Invocations => invocations;
        public bool TryAddInvocation(MethodDefinition method) => invocations.TryAdd(method.GetIdentifier(), method);
        public bool AddCombinedFrom(DelegateInvocationData other) {

            if (other.invocations == invocations) {
                return false;
            }

            bool anyModifered = false;

            foreach (var invocation in invocations) {
                if (other.invocations.TryAdd(invocation.Key, invocation.Value)) {
                    anyModifered = true;
                }
            }

            var datasFromMe = combinedFromMap[invocations];
            combinedFromMap.Remove(invocations);

            invocations = other.invocations;

            var datasFromOther = combinedFromMap[other.invocations];

            // Make sure the invocations are pointing to the same dictionary instance
            // so that modifications to one will naturally affect the other
            foreach (var oldData in datasFromMe) {

                if (datasFromOther.Add(oldData)) {
                    anyModifered = true;
                }

                if (oldData.invocations == other.invocations) {
                    continue;
                }

                oldData.invocations = other.invocations;
                anyModifered = true;
            }
            datasFromMe.Clear();
            return anyModifered;
        }

        public static string GenerateStackKey(MethodDefinition method, Instruction loadDelegateInst) {
            if (loadDelegateInst.OpCode == OpCodes.Ldfld || loadDelegateInst.OpCode == OpCodes.Ldsfld) {
                var field = (FieldReference)loadDelegateInst.Operand;
                return $"Field:{field.DeclaringType.FullName}:{field.Name}";
            }
            if (MonoModCommon.IL.TryGetReferencedParameter(method, loadDelegateInst, out var parameter)) {
                return $"Param:{method.GetIdentifier()}:{parameter.GetDebugName()}";
            }
            if (method.HasBody) {
                if (MonoModCommon.IL.TryGetReferencedVariable(method, loadDelegateInst, out var variable)) {
                    return $"Variable:{method.GetIdentifier()}:V_{variable.Index}";
                }
            }
            return $"Others:{method.GetIdentifier()}:IL_{loadDelegateInst.Offset}";
        }

        public override int GetHashCode() => Key.GetHashCode();
        public override bool Equals(object? obj) {
            if (obj is not DelegateInvocationData other) return false;
            return other.Key == Key;
        }
        public override string ToString() {
            return Key;
        }
    }
}
