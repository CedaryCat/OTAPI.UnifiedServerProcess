using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class CompositeParameterTracking {
        public readonly Dictionary<string, ParameterTrackingManifest> ReferencedParameters = [];


        public CompositeParameterTracking() { }
        public CompositeParameterTracking CreateFromStoreSelfAsMember(MemberReference newMember) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateFromStoreSelfAsMember(newMember))));
            }
            return result;
        }
        public CompositeParameterTracking CreateFromStoreSelfAsMember(FieldReference newMember)
            => CreateFromStoreSelfAsMember((MemberReference)newMember);
        public CompositeParameterTracking CreateFromStoreSelfAsArrayElement(ArrayType arrayType) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateFromStoreSelfAsElement(arrayType))));
            }
            return result;
        }
        public CompositeParameterTracking CreateFromStoreSelfAsCollectionElement(TypeReference collectionType, TypeReference elementType) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateFromStoreSelfAsCollectionElement(collectionType, elementType))));
            }
            return result;
        }
        public CompositeParameterTracking? CreateFromStoreSelfInEnumerator() {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateFromStoreSelfInEnumerator())
                        .Where(chain => chain != null)
                        .OfType<ParameterTrackingChain>()));
            }
            if (result.ReferencedParameters.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryTrackMemberLoad(MemberReference member, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendWithMemberLoad(member, out ParameterTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterTrackingManifest(originGroup.Value.TrackedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackArrayElementLoad(ArrayType arrayType, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendWithArrayElementLoad(arrayType, out ParameterTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterTrackingManifest(originGroup.Value.TrackedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackCollectionElementLoad(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryTrackCollectionElementLoad(collectionType, elementType, out ParameterTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterTrackingManifest(originGroup.Value.TrackedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryTrackEnumeratorCurrent(out ParameterTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterTrackingManifest(originGroup.Value.TrackedParameter, newChains);
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
