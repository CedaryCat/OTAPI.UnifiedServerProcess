using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class CompositeStaticFieldTracking
    {
        public readonly Dictionary<string, SingleStaticFieldTrace> TrackedStaticFields = [];

        public CompositeStaticFieldTracking() { }
        public CompositeStaticFieldTracking CreateEncapsulatedInstance(MemberReference newMember) {
            var result = new CompositeStaticFieldTracking();
            foreach (var origin in TrackedStaticFields) {
                result.TrackedStaticFields.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.TrackingStaticField,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember))));
            }
            return result;
        }
        public CompositeStaticFieldTracking CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public CompositeStaticFieldTracking CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            var result = new CompositeStaticFieldTracking();
            foreach (var origin in TrackedStaticFields) {
                result.TrackedStaticFields.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.TrackingStaticField,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType))));
            }
            return result;
        }
        public CompositeStaticFieldTracking CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            var result = new CompositeStaticFieldTracking();
            foreach (var origin in TrackedStaticFields) {
                result.TrackedStaticFields.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.TrackingStaticField,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType))));
            }
            return result;
        }
        public CompositeStaticFieldTracking? CreateEncapsulatedEnumeratorInstance() {
            var result = new CompositeStaticFieldTracking();
            foreach (var origin in TrackedStaticFields) {
                result.TrackedStaticFields.Add(
                    origin.Key,
                    new SingleStaticFieldTrace(
                        origin.Value.TrackingStaticField,
                        origin.Value.PartTrackingPaths.Select(chain => chain.CreateEncapsulatedEnumeratorInstance())
                        .Where(chain => chain != null)
                        .OfType<StaticFieldTrackingChain>()));
            }
            if (result.TrackedStaticFields.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryExtendTrackingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out CompositeStaticFieldTracking? resultTrace) {
            resultTrace = new CompositeStaticFieldTracking();
            bool foundAny = false;

            foreach (var originGroup in TrackedStaticFields) {
                var newChains = new HashSet<StaticFieldTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithMemberAccess(member, out StaticFieldTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TrackedStaticFields[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.TrackingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryExtendTrackingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out CompositeStaticFieldTracking? resultTrace) {
            resultTrace = new CompositeStaticFieldTracking();
            bool foundAny = false;

            foreach (var originGroup in TrackedStaticFields) {
                var newChains = new HashSet<StaticFieldTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithArrayAccess(arrayType, out StaticFieldTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TrackedStaticFields[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.TrackingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryExtendTrackingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out CompositeStaticFieldTracking? resultTrace) {
            resultTrace = new CompositeStaticFieldTracking();
            bool foundAny = false;

            foreach (var originGroup in TrackedStaticFields) {
                var newChains = new HashSet<StaticFieldTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryExtendTrackingWithCollectionAccess(collectionType, elementType, out StaticFieldTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TrackedStaticFields[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.TrackingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTrackEnumeratorCurrent([NotNullWhen(true)] out CompositeStaticFieldTracking? resultTrace) {
            resultTrace = new CompositeStaticFieldTracking();
            bool foundAny = false;

            foreach (var originGroup in TrackedStaticFields) {
                var newChains = new HashSet<StaticFieldTrackingChain>();

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (chain.TryTrackEnumeratorCurrent(out StaticFieldTrackingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TrackedStaticFields[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.TrackingStaticField, newChains);
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
