using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class ParameterOriginChain : IEquatable<ParameterOriginChain> {

        public readonly ParameterDefinition SourceParameter;
        public readonly ImmutableArray<MemberLayer> MemberAccessChain;
        readonly string Key;

        public ParameterOriginChain(ParameterDefinition sourceParameter, IEnumerable<MemberLayer> accessChain) {
            SourceParameter = sourceParameter ?? throw new ArgumentNullException(nameof(sourceParameter));
            MemberAccessChain = accessChain?.ToImmutableArray() ?? [];
            Key = ToString();
        }
        private ParameterOriginChain(ParameterOriginChain baseChain, MemberLayer newMember) {
            SourceParameter = baseChain.SourceParameter;
            MemberAccessChain = baseChain.MemberAccessChain.Insert(0, newMember);
            Key = ToString();
        }
        public ParameterOriginChain CreateFromStoreSelfAsElement(ArrayType arrayType)
            => new(this, new ArrayElementLayer(arrayType));
        public ParameterOriginChain CreateFromStoreSelfAsMember(MemberReference newMember)
            => new(this, newMember);
        public ParameterOriginChain CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType)
            => new(this, new CollectionElementLayer(collectionType, elementType));
        public ParameterOriginChain? CreateFromStoreSelfInEnumerator() {
            if (MemberAccessChain.IsEmpty) {
                return null;
            }
            if (MemberAccessChain[0] is ArrayElementLayer or CollectionElementLayer) {
                var collectionEle = MemberAccessChain[0];
                return new ParameterOriginChain(this, new EnumeratorLayer(collectionEle.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (MemberAccessChain[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }
        public override string ToString() {
            var name = SourceParameter.GetDebugName();
            return MemberAccessChain.IsEmpty ?
                $"[{name}]" :
                $"[{name} = {MemberAccessChain[0].DeclaringType.FullName + "::" + string.Join(".", MemberAccessChain.Select(m => m.Name))}]";
        }

        public bool TryExtendWithMemberLoad(MemberReference member, [NotNullWhen(true)] out ParameterOriginChain? result)
            => TryExtendWithMemberLoad((MemberLayer)member, out result);
        public bool TryExtendWithArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out ParameterOriginChain? result) {
            var indexer = new ArrayElementLayer(arrayType);
            return TryExtendWithMemberLoad(indexer, out result);
        }
        private bool TryExtendWithMemberLoad(MemberLayer member, [NotNullWhen(true)] out ParameterOriginChain? result) {
            result = null;

            if (MemberAccessChain.IsEmpty) {

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
                    // it's means that member is a reference part of the argument
                    // so we treat it as the argument itself
                    // and the call chain is empty

                    result = new ParameterOriginChain(SourceParameter, []);
                    return true;
                }
                return false;
            }

            if (member.IsSameLayer(MemberAccessChain[0])) {
                result = new ParameterOriginChain(
                    SourceParameter,
                    MemberAccessChain.RemoveAt(0)
                );
                return true;
            }

            return false;
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out ParameterOriginChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryExtendWithMemberLoad(collection, out result);
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out ParameterOriginChain? result) {
            if (MemberAccessChain.Length < 2) {
                result = null;
                return false;
            }
            if (MemberAccessChain[0] is not EnumeratorLayer) {
                result = null;
                return false;
            }
            if (MemberAccessChain[1] is not ArrayElementLayer && MemberAccessChain[1] is not CollectionElementLayer) {
                throw new NotSupportedException("Enumerator layer must be followed by ArrayElementLayer or CollectionElementLayer.");
            }
            result = new ParameterOriginChain(
                SourceParameter,
                MemberAccessChain.RemoveAt(0).RemoveAt(0)
            );
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ParameterOriginChain);
        public bool Equals(ParameterOriginChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
