using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class CompositeParameterTracking {
        public readonly Dictionary<string, ParameterTrackingManifest> ReferencedParameters = [];


        public CompositeParameterTracking() { }
        public CompositeParameterTracking CreateEncapsulatedInstance(MemberReference newMember) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember))));
            }
            return result;
        }
        public CompositeParameterTracking CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public CompositeParameterTracking CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType))));
            }
            return result;
        }
        public CompositeParameterTracking CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType))));
            }
            return result;
        }
        public CompositeParameterTracking? CreateEncapsulatedEnumeratorInstance() {
            var result = new CompositeParameterTracking();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterTrackingManifest(
                        origin.Value.TrackedParameter,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedEnumeratorInstance())
                        .Where(chain => chain != null)
                        .OfType<ParameterTrackingChain>()));
            }
            if (result.ReferencedParameters.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryExtendTrackingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithMemberAccess(member, out ParameterTrackingChain? newChain)) {
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
        public bool TryExtendTrackingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithArrayAccess(arrayType, out ParameterTrackingChain? newChain)) {
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
        public bool TryExtendTrackingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out CompositeParameterTracking? resultTrace) {
            resultTrace = new CompositeParameterTracking();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithCollectionAccess(collectionType, elementType, out ParameterTrackingChain? newChain)) {
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
