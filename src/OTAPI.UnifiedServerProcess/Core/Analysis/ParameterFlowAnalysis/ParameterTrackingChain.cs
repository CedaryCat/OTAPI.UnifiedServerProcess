using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class ParameterTrackingChain : IEquatable<ParameterTrackingChain> {
        /// <summary>
        /// The original parameter definition being tracked
        /// </summary>

        public readonly ParameterDefinition TrackingParameter;
        /// <summary>
        /// When a parameter is encapsulated within an instance, the storeIn containing the parameter
        /// is prepended to this hierarchy in the chain replica created by <see cref="CreateEncapsulatedInstance"/>.
        /// Subsequent encapsulations will continue prepending members. This hierarchy represents the path
        /// from outermost container to inner values needed to access the tracked parameter.
        /// 
        /// <para>When accessing members from tracked objects via methods like <see cref="TryExtendTrackingWithMemberAccess"/>:</para>
        /// <para>1. If accessed storeIn matches the first element in the hierarchy, it indicates unwrapping - returns true</para>
        /// <para>2. If storeIn doesn't match hierarchy head - indicates unrelated access - returns false</para>
        /// <para>3. Empty hierarchy means tracking base parameter. Member accesses are considered part of
        /// the parameter itself and extend <see cref="ComponentAccessPath"/> instead - returns true</para>
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> EncapsulationHierarchy = [];
        /// <summary>
        /// When directly accessing members of the base parameter (when <see cref="EncapsulationHierarchy"/> is empty),
        /// these accesses are recorded here. Unlike the encapsulation hierarchy, this chain represents the internal
        /// structure of the parameter itself and doesn't support unwrapping semantics.
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> ComponentAccessPath = [];
        readonly string Key;

        public ParameterTrackingChain(ParameterDefinition sourceParameter, IEnumerable<MemberAccessStep> encapsulationHierarchy, IEnumerable<MemberAccessStep> componentAccessPath) {
            TrackingParameter = sourceParameter ?? throw new ArgumentNullException(nameof(sourceParameter));
            EncapsulationHierarchy = encapsulationHierarchy?.ToImmutableArray() ?? [];
            ComponentAccessPath = componentAccessPath?.ToImmutableArray() ?? [];
            Key = ToString();
        }
        private ParameterTrackingChain(ParameterTrackingChain baseChain, MemberAccessStep encapsulationLayer) {
            TrackingParameter = baseChain.TrackingParameter;
            EncapsulationHierarchy = baseChain.EncapsulationHierarchy.Insert(0, encapsulationLayer);
            Key = ToString();
        }
        public ParameterTrackingChain CreateFromStoreSelfAsElement(ArrayType arrayType)
            => new(this, new ArrayElementLayer(arrayType));
        public ParameterTrackingChain CreateFromStoreSelfAsMember(MemberReference storeIn)
            => new(this, storeIn);
        public ParameterTrackingChain CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType)
            => new(this, new CollectionElementLayer(collectionType, elementType));
        public ParameterTrackingChain? CreateFromStoreSelfInEnumerator() {
            if (EncapsulationHierarchy.IsEmpty) {
                return null;
            }
            if (EncapsulationHierarchy[0] is ArrayElementLayer or CollectionElementLayer) {
                var collectionEle = EncapsulationHierarchy[0];
                return new ParameterTrackingChain(this, new EnumeratorLayer(collectionEle.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (EncapsulationHierarchy[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }
        public override string ToString() {
            var name = TrackingParameter.GetDebugName();
            return EncapsulationHierarchy.IsEmpty ?
                $"[{name}]" :
                $"[{name} = {EncapsulationHierarchy[0].DeclaringType.FullName + "::" + string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}]";
        }

        public bool TryExtendWithMemberLoad(MemberReference member, [NotNullWhen(true)] out ParameterTrackingChain? result)
            => TryExtendWithMemberLoad((MemberAccessStep)member, out result);
        public bool TryExtendWithArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out ParameterTrackingChain? result) {
            var indexer = new ArrayElementLayer(arrayType);
            return TryExtendWithMemberLoad(indexer, out result);
        }
        private bool TryExtendWithMemberLoad(MemberAccessStep member, [NotNullWhen(true)] out ParameterTrackingChain? result) {
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
                else {
                    isValidReference = false;
                }

                if (isValidReference) {

                    // ** very special case **
                    // it's means that storeIn is a reference part of the argument
                    // so we treat it as the argument itself
                    // and the encapsulation hierarchy is empty

                    result = new ParameterTrackingChain(TrackingParameter, [], [..ComponentAccessPath, member]);
                    return true;
                }
                return false;
            }

            if (member.IsSameLayer(EncapsulationHierarchy[0])) {
                result = new ParameterTrackingChain(
                    TrackingParameter,
                    EncapsulationHierarchy.RemoveAt(0),
                    ComponentAccessPath
                );
                return true;
            }

            return false;
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out ParameterTrackingChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryExtendWithMemberLoad(collection, out result);
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out ParameterTrackingChain? result) {
            if (EncapsulationHierarchy.Length < 2) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[0] is not EnumeratorLayer) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[1] is not ArrayElementLayer && EncapsulationHierarchy[1] is not CollectionElementLayer) {
                throw new NotSupportedException("Enumerator layer must be followed by ArrayElementLayer or CollectionElementLayer.");
            }
            result = new ParameterTrackingChain(
                TrackingParameter,
                EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                ComponentAccessPath
            );
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ParameterTrackingChain);
        public bool Equals(ParameterTrackingChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
