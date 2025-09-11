using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    public sealed class ParameterTracingChain : IEquatable<ParameterTracingChain>
    {
        /// <summary>
        /// The original Parameter definition being traced
        /// </summary>

        public readonly ParameterDefinition TracingParameter;
        /// <summary>
        /// When a Parameter is encapsulated within an instance, the storeIn containing the Parameter
        /// is prepended to this hierarchy in the tail replica created by <see cref="CreateEncapsulatedInstance"/>.
        /// Subsequent encapsulations will continue prepending members. This hierarchy represents the path
        /// from outermost container to inner values needed to access the traced Parameter.
        /// 
        /// <para>When accessing members from traced objects via methods like <see cref="TryExtendTracingWithMemberAccess"/>:</para>
        /// <para>1. If accessed storeIn matches the first element in the hierarchy, it indicates unwrapping - returns true</para>
        /// <para>2. If storeIn doesn't match hierarchy head - indicates unrelated access - returns false</para>
        /// <para>3. Empty hierarchy means tracing base Parameter. Member accesses are considered part of
        /// the Parameter itself and extend <see cref="ComponentAccessPath"/> instead - returns true</para>
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> EncapsulationHierarchy = [];
        /// <summary>
        /// When directly accessing members of the base Parameter (when <see cref="EncapsulationHierarchy"/> is empty),
        /// these accesses are recorded here. Unlike the encapsulation hierarchy, this tail represents the internal
        /// structure of the Parameter itself and doesn't support unwrapping semantics.
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> ComponentAccessPath = [];
        readonly string Key;

        public ParameterTracingChain(ParameterDefinition sourceParameter, IEnumerable<MemberAccessStep> encapsulationHierarchy, IEnumerable<MemberAccessStep> componentAccessPath) {
            TracingParameter = sourceParameter ?? throw new ArgumentNullException(nameof(sourceParameter));
            EncapsulationHierarchy = encapsulationHierarchy?.ToImmutableArray() ?? [];
            ComponentAccessPath = componentAccessPath?.ToImmutableArray() ?? [];
            Key = ToString();
        }
        private ParameterTracingChain(ParameterTracingChain baseChain, MemberAccessStep encapsulationLayer) {
            TracingParameter = baseChain.TracingParameter;
            EncapsulationHierarchy = baseChain.EncapsulationHierarchy.Insert(0, encapsulationLayer);
            Key = ToString();
        }
        public ParameterTracingChain CreateEncapsulatedArrayInstance(ArrayType arrayType)
            => new(this, new ArrayElementLayer(arrayType));
        public ParameterTracingChain CreateEncapsulatedInstance(MemberReference storeIn)
            => new(this, storeIn);
        public ParameterTracingChain CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType)
            => new(this, new CollectionElementLayer(collectionType, elementType));
        public ParameterTracingChain? CreateEncapsulatedEnumeratorInstance() {
            if (EncapsulationHierarchy.IsEmpty) {
                return null;
            }
            if (EncapsulationHierarchy[0] is ArrayElementLayer or CollectionElementLayer) {
                var collectionEle = EncapsulationHierarchy[0];
                return new ParameterTracingChain(this, new EnumeratorLayer(collectionEle.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (EncapsulationHierarchy[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }
        public override string ToString() {
            var paramName = TracingParameter.GetDebugName();
            if (!EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${paramName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else if (!EncapsulationHierarchy.IsEmpty && ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${paramName} }}";
            }
            else if (EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ ${paramName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else {
                return $"{{ ${paramName} }}";
            }
        }

        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryExtendTracingWithMemberAccess((MemberAccessStep)member, out result);
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out ParameterTracingChain? result) {
            var indexer = new ArrayElementLayer(arrayType);
            return TryExtendTracingWithMemberAccess(indexer, out result);
        }
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out ParameterTracingChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryExtendTracingWithMemberAccess(collection, out result);
        }
        private bool TryExtendTracingWithMemberAccess(MemberAccessStep member, [NotNullWhen(true)] out ParameterTracingChain? result) {
            result = null;

            if (EncapsulationHierarchy.IsEmpty) {

                bool isValidReference;

                if (member is RealMemberLayer realMember) {
                    isValidReference = realMember.Member switch {
                        MethodReference m => !m.ReturnType.IsTruelyValueType(),
                        FieldReference f => !f.FieldType.IsTruelyValueType(),
                        PropertyReference p => !p.PropertyType.IsTruelyValueType(),
                        _ => false
                    };
                }
                else if (member is ArrayElementLayer indexer) {
                    isValidReference = !indexer.MemberType.IsTruelyValueType();
                }
                else if (member is CollectionElementLayer collection) {
                    isValidReference = !collection.MemberType.IsTruelyValueType();
                }
                else {
                    isValidReference = false;
                }

                if (isValidReference) {

                    // ** very special case **
                    // it's means that storeIn is a reference part of the argument
                    // so we treat it as the argument itself
                    // and the encapsulation hierarchy is empty

                    result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
                    return true;
                }
                return false;
            }

            if (member.IsSameLayer(EncapsulationHierarchy[0])) {
                result = new ParameterTracingChain(
                    TracingParameter,
                    EncapsulationHierarchy.RemoveAt(0),
                    ComponentAccessPath
                );
                return true;
            }

            return false;
        }
        public bool TryTraceEnumeratorCurrent([NotNullWhen(true)] out ParameterTracingChain? result) {
            if (EncapsulationHierarchy.Length < 2) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[0] is not EnumeratorLayer) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[1] is not CollectionElementLayer) {
                throw new NotSupportedException("Enumerator layer must be followed by ArrayElementLayer or CollectionElementLayer.");
            }
            result = new ParameterTracingChain(
                TracingParameter,
                EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                ComponentAccessPath
            );
            return true;
        }
        public static ParameterTracingChain? CombineParameterTraces(ParameterTracingChain outerPart, ParameterTracingChain innerPart) {
            static bool AreEqualLengthSegmentsEqual(ImmutableArray<MemberAccessStep> a, ImmutableArray<MemberAccessStep> b) {
                var length = Math.Min(a.Length, b.Length);
                for (int i = 0; i < length; i++) {
                    if (!a[i].IsSameLayer(b[i])) {
                        return false;
                    }
                }
                return true;
            }
            if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                if (!AreEqualLengthSegmentsEqual(innerPart.ComponentAccessPath, outerPart.EncapsulationHierarchy)) {
                    return null;
                }
                if (innerPart.ComponentAccessPath.Length < outerPart.EncapsulationHierarchy.Length) {
                    return new ParameterTracingChain(outerPart.TracingParameter,
                        [.. innerPart.EncapsulationHierarchy, .. outerPart.EncapsulationHierarchy.Skip(innerPart.ComponentAccessPath.Length)],
                        []
                    );
                }
                else if (innerPart.ComponentAccessPath.Length > outerPart.EncapsulationHierarchy.Length) {
                    return new ParameterTracingChain(outerPart.TracingParameter,
                        innerPart.EncapsulationHierarchy,
                        innerPart.ComponentAccessPath.Skip(outerPart.EncapsulationHierarchy.Length)
                    );
                }
                else {
                    return new ParameterTracingChain(outerPart.TracingParameter,
                        innerPart.EncapsulationHierarchy,
                        []
                    );
                }
            }
            else if (innerPart.ComponentAccessPath.Length == 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                return new ParameterTracingChain(outerPart.TracingParameter,
                    [.. innerPart.EncapsulationHierarchy, .. outerPart.EncapsulationHierarchy],
                    outerPart.ComponentAccessPath
                );
            }
            else if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length == 0) {
                return new ParameterTracingChain(outerPart.TracingParameter,
                    [],
                    [.. outerPart.ComponentAccessPath, .. innerPart.ComponentAccessPath]
                );
            }
            else {
                return new ParameterTracingChain(outerPart.TracingParameter,
                    [],
                    []
                );
            }
        }
        public override bool Equals(object? obj) => Equals(obj as ParameterTracingChain);
        public bool Equals(ParameterTracingChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
