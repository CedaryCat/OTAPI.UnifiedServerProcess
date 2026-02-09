using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis
{
    public class MethodCallData(MethodDefinition method, MethodReferenceData[] usedMethods, MethodDefinition[] usedByMethods)
    {
        public readonly MethodDefinition method = method;
        public readonly MethodReferenceData[] UsedMethods = usedMethods;
        public readonly MethodDefinition[] UsedByMethods = usedByMethods;
    }
    public readonly struct MethodReferenceData(MethodDefinition directlyCalledMethod, MethodDefinition[] implicitlyCalledMethods, ImplicitCallMode implicitCallMode) : IEquatable<MethodReferenceData>
    {
        public readonly MethodDefinition DirectlyCalledMethod = directlyCalledMethod;
        public readonly MethodDefinition[] ImplicitlyCalledMethods = implicitlyCalledMethods;
        public readonly ImplicitCallMode implicitCallMode = implicitCallMode;
        public static MethodReferenceData NormalCall(MethodDefinition directlyCalledMethod) => new(directlyCalledMethod, [], ImplicitCallMode.None);
        public static MethodReferenceData InheritanceCall(MethodDefinition directlyCalledMethod, MethodDefinition[] implmentedMethods) => new(directlyCalledMethod, implmentedMethods, ImplicitCallMode.Inheritance);
        public static MethodReferenceData DelegateCall(MethodDefinition directlyCalledMethod, MethodDefinition[] implmentedMethods) => new(directlyCalledMethod, implmentedMethods, ImplicitCallMode.Delegate);
        public readonly IEnumerable<MethodDefinition> ImplementedMethods() {
            if (implicitCallMode == ImplicitCallMode.None) {
                yield return DirectlyCalledMethod;
                yield break;
            }
            if (implicitCallMode == ImplicitCallMode.Inheritance && DirectlyCalledMethod.HasBody) {
                yield return DirectlyCalledMethod;
            }
            foreach (MethodDefinition method in ImplicitlyCalledMethods) {
                if (method.HasBody) {
                    yield return method;
                }
            }
        }
        public override readonly int GetHashCode() {
            return DirectlyCalledMethod.GetIdentifier().GetHashCode();
        }
        public readonly bool Equals(MethodReferenceData other) => DirectlyCalledMethod.GetIdentifier() == other.DirectlyCalledMethod.GetIdentifier();
        public override readonly string ToString() {
            return $"{DirectlyCalledMethod.GetDebugName()} ({implicitCallMode}, ImplicitCount: {ImplicitlyCalledMethods.Length})";
        }
    }
    public enum ImplicitCallMode
    {
        None,
        Inheritance,
        Delegate
    }
}
