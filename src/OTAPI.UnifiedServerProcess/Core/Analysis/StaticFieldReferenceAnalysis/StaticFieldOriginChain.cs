using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis {
    public class StaticFieldOriginChain : IEquatable<StaticFieldOriginChain> {
        public readonly FieldDefinition SourceStaticField;
        public readonly ImmutableArray<MemberLayer> MemberAccessChain;
        readonly string Key;

        public StaticFieldOriginChain(FieldDefinition sourceStataicField, IEnumerable<MemberLayer> accessChain) {
            SourceStaticField = sourceStataicField ?? throw new ArgumentNullException(nameof(sourceStataicField));
            MemberAccessChain = accessChain?.ToImmutableArray() ?? [];
            Key = ToString();
        }

        public StaticFieldOriginChain(StaticFieldOriginChain baseChain, MethodReference newMember) : this(baseChain, (MemberLayer)newMember) { }
        private StaticFieldOriginChain(StaticFieldOriginChain baseChain, MemberLayer newMember) {
            SourceStaticField = baseChain.SourceStaticField;
            MemberAccessChain = baseChain.MemberAccessChain.Insert(0, newMember);
            Key = ToString();
        }

        public override string ToString() {
            var name = SourceStaticField.FullName;
            return MemberAccessChain.IsEmpty ?
                $"[{name}]" :
                $"[{name} = {MemberAccessChain[0].DeclaringType.FullName + "::" + string.Join(".", MemberAccessChain.Select(m => m.Name))}]";
        }

        public bool TryExtendWithMemberLoad(MemberReference member, [NotNullWhen(true)] out StaticFieldOriginChain? result)
            => TryExtendWithMemberLoad((MemberLayer)member, out result);
        private bool TryExtendWithMemberLoad(MemberLayer member, [NotNullWhen(true)] out StaticFieldOriginChain? result) {
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

                    result = new StaticFieldOriginChain(SourceStaticField, []);
                    return true;
                }
                return false;
            }

            if (member.IsSameLayer(MemberAccessChain[0])) {
                result = new StaticFieldOriginChain(
                    SourceStaticField,
                    MemberAccessChain.RemoveAt(0)
                );
                return true;
            }

            return false;
        }
        public bool TryExtendWithArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out StaticFieldOriginChain? result) {
            var array = new ArrayElementLayer(arrayType);
            return TryExtendWithMemberLoad(array, out result);
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out StaticFieldOriginChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryExtendWithMemberLoad(collection, out result);
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out StaticFieldOriginChain? result) {
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
            result = new StaticFieldOriginChain(
                SourceStaticField,
                MemberAccessChain.RemoveAt(0).RemoveAt(0)
            );
            return true;
        }

        public StaticFieldOriginChain CreateFromStoreSelfAsMember(MemberReference newMember)
            => new(this, (MemberLayer)newMember);
        public StaticFieldOriginChain CreateFromStoreSelfAsArrayElement(ArrayType arrayType)
            => new(this, new ArrayElementLayer(arrayType));
        public StaticFieldOriginChain CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType)
            => new(this, new CollectionElementLayer(collectionType, elementType));
        public StaticFieldOriginChain? CreateFromStoreSelfInEnumerator() {
            if (MemberAccessChain.IsEmpty) {
                return null;
            }
            if (MemberAccessChain[0] is ArrayElementLayer or CollectionElementLayer) {
                var collectionEle = MemberAccessChain[0];
                return new StaticFieldOriginChain(this, new EnumeratorLayer(collectionEle.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (MemberAccessChain[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }

        public override bool Equals(object? obj) => Equals(obj as StaticFieldOriginChain);
        public bool Equals(StaticFieldOriginChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
