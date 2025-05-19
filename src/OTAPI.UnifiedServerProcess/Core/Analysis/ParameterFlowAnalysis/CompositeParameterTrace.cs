using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class CompositeParameterTrace {
        public readonly Dictionary<string, SingleParameterTrace> ParameterOrigins = [];


        public CompositeParameterTrace() { }
        public CompositeParameterTrace CreateFromStoreSelfAsMember(MemberReference newMember) {
            var result = new CompositeParameterTrace();
            foreach (var origin in ParameterOrigins) {
                result.ParameterOrigins.Add(
                    origin.Key,
                    new SingleParameterTrace(
                        origin.Value.SourceParameter,
                        origin.Value.ParameterOrigins.Select(chain => chain.CreateFromStoreSelfAsMember(newMember))));
            }
            return result;
        }
        public CompositeParameterTrace CreateFromStoreSelfAsMember(FieldReference newMember)
            => CreateFromStoreSelfAsMember((MemberReference)newMember);
        public CompositeParameterTrace CreateFromStoreSelfAsArrayElement(ArrayType arrayType) {
            var result = new CompositeParameterTrace();
            foreach (var origin in ParameterOrigins) {
                result.ParameterOrigins.Add(
                    origin.Key,
                    new SingleParameterTrace(
                        origin.Value.SourceParameter,
                        origin.Value.ParameterOrigins.Select(chain => chain.CreateFromStoreSelfAsElement(arrayType))));
            }
            return result;
        }
        public CompositeParameterTrace CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType) {
            var result = new CompositeParameterTrace();
            foreach (var origin in ParameterOrigins) {
                result.ParameterOrigins.Add(
                    origin.Key,
                    new SingleParameterTrace(
                        origin.Value.SourceParameter,
                        origin.Value.ParameterOrigins.Select(chain => chain.CreateFromStoreSelfAsCollectionElement(collectionType, elementType))));
            }
            return result;
        }
        public CompositeParameterTrace? CreateFromStoreSelfInEnumerator() {
            var result = new CompositeParameterTrace();
            foreach (var origin in ParameterOrigins) {
                result.ParameterOrigins.Add(
                    origin.Key,
                    new SingleParameterTrace(
                        origin.Value.SourceParameter,
                        origin.Value.ParameterOrigins.Select(chain => chain.CreateFromStoreSelfInEnumerator())
                        .Where(chain => chain != null)
                        .OfType<ParameterOriginChain>()));
            }
            if (result.ParameterOrigins.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryTrackMemberLoad(MemberReference member, [NotNullWhen(true)] out CompositeParameterTrace? resultTrace) {
            resultTrace = new CompositeParameterTrace();
            bool foundAny = false;

            foreach (var originGroup in ParameterOrigins) {
                var newChains = new HashSet<ParameterOriginChain>();

                foreach (var chain in originGroup.Value.ParameterOrigins) {
                    if (chain.TryExtendWithMemberLoad(member, out ParameterOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ParameterOrigins[originGroup.Key] = new SingleParameterTrace(originGroup.Value.SourceParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out CompositeParameterTrace? resultTrace) {
            resultTrace = new CompositeParameterTrace();
            bool foundAny = false;

            foreach (var originGroup in ParameterOrigins) {
                var newChains = new HashSet<ParameterOriginChain>();

                foreach (var chain in originGroup.Value.ParameterOrigins) {
                    if (chain.TryExtendWithArrayElementLoad(arrayType, out ParameterOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ParameterOrigins[originGroup.Key] = new SingleParameterTrace(originGroup.Value.SourceParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out CompositeParameterTrace? resultTrace) {
            resultTrace = new CompositeParameterTrace();
            bool foundAny = false;

            foreach (var originGroup in ParameterOrigins) {
                var newChains = new HashSet<ParameterOriginChain>();

                foreach (var chain in originGroup.Value.ParameterOrigins) {
                    if (chain.TryTrackCollectionElementLoad(collectionType, elementType, out ParameterOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ParameterOrigins[originGroup.Key] = new SingleParameterTrace(originGroup.Value.SourceParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out CompositeParameterTrace? resultTrace) {
            resultTrace = new CompositeParameterTrace();
            bool foundAny = false;

            foreach (var originGroup in ParameterOrigins) {
                var newChains = new HashSet<ParameterOriginChain>();

                foreach (var chain in originGroup.Value.ParameterOrigins) {
                    if (chain.TryTrackEnumeratorCurrent(out ParameterOriginChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ParameterOrigins[originGroup.Key] = new SingleParameterTrace(originGroup.Value.SourceParameter, newChains);
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
