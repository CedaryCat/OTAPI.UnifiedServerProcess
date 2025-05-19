using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis {
    public class CompositeStaticFieldTrace {
        public readonly Dictionary<string, SingleStaticFieldTrace> StaticFieldOrigins = [];

        public CompositeStaticFieldTrace() { }
        public CompositeStaticFieldTrace CreateFromStoreSelfAsMember(MemberReference newMember) {
            var result = new CompositeStaticFieldTrace();
            foreach (var origin in StaticFieldOrigins) {
                result.StaticFieldOrigins.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.SourceStaticField,
                        origin.Value.StaticFieldOrigins.Select(chain => chain.CreateFromStoreSelfAsMember(newMember))));
            }
            return result;
        }
        public CompositeStaticFieldTrace CreateFromStoreSelfAsMember(FieldReference newMember)
            => CreateFromStoreSelfAsMember((MemberReference)newMember);
        public CompositeStaticFieldTrace CreateFromStoreSelfAsArrayElement(ArrayType arrayType) {
            var result = new CompositeStaticFieldTrace();
            foreach (var origin in StaticFieldOrigins) {
                result.StaticFieldOrigins.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.SourceStaticField,
                        origin.Value.StaticFieldOrigins.Select(chain => chain.CreateFromStoreSelfAsArrayElement(arrayType))));
            }
            return result;
        }
        public CompositeStaticFieldTrace CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType) {
            var result = new CompositeStaticFieldTrace();
            foreach (var origin in StaticFieldOrigins) {
                result.StaticFieldOrigins.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.SourceStaticField,
                        origin.Value.StaticFieldOrigins.Select(chain => chain.CreateFromStoreSelfAsCollectionElement(collectionType, elementType))));
            }
            return result;
        }
        public CompositeStaticFieldTrace? CreateFromStoreSelfInEnumerator() {
            var result = new CompositeStaticFieldTrace();
            foreach (var origin in StaticFieldOrigins) {
                result.StaticFieldOrigins.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.SourceStaticField,
                        origin.Value.StaticFieldOrigins.Select(chain => chain.CreateFromStoreSelfInEnumerator())
                        .Where(chain => chain != null)
                        .OfType<StaticFieldOriginChain>()));
            }
            if (result.StaticFieldOrigins.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryTrackMemberLoad(MemberReference member, [NotNullWhen(true)] out CompositeStaticFieldTrace? resultTrace) {
            resultTrace = new CompositeStaticFieldTrace();
            bool foundAny = false;

            foreach (var originGroup in StaticFieldOrigins) {
                var newChains = new HashSet<StaticFieldOriginChain>();

                foreach (var chain in originGroup.Value.StaticFieldOrigins) {
                    if (chain.TryExtendWithMemberLoad(member, out StaticFieldOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.StaticFieldOrigins[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.SourceStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out CompositeStaticFieldTrace? resultTrace) {
            resultTrace = new CompositeStaticFieldTrace();
            bool foundAny = false;

            foreach (var originGroup in StaticFieldOrigins) {
                var newChains = new HashSet<StaticFieldOriginChain>();

                foreach (var chain in originGroup.Value.StaticFieldOrigins) {
                    if (chain.TryExtendWithArrayElementLoad(arrayType, out StaticFieldOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.StaticFieldOrigins[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.SourceStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out CompositeStaticFieldTrace? resultTrace) {
            resultTrace = new CompositeStaticFieldTrace();
            bool foundAny = false;

            foreach (var originGroup in StaticFieldOrigins) {
                var newChains = new HashSet<StaticFieldOriginChain>();

                foreach (var chain in originGroup.Value.StaticFieldOrigins) {
                    if (chain.TryTrackCollectionElementLoad(collectionType, elementType, out StaticFieldOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.StaticFieldOrigins[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.SourceStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out CompositeStaticFieldTrace? resultTrace) {
            resultTrace = new CompositeStaticFieldTrace();
            bool foundAny = false;

            foreach (var originGroup in StaticFieldOrigins) {
                var newChains = new HashSet<StaticFieldOriginChain>();

                foreach (var chain in originGroup.Value.StaticFieldOrigins) {
                    if (chain.TryTrackEnumeratorCurrent(out StaticFieldOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.StaticFieldOrigins[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.SourceStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
    }
}
