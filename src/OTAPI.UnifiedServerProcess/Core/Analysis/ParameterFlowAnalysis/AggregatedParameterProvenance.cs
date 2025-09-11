using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    public sealed class AggregatedParameterProvenance
    {
        public readonly Dictionary<string, ParameterProvenance> ReferencedParameters = [];


        public AggregatedParameterProvenance() { }
        public AggregatedParameterProvenance CreateEncapsulatedInstance(MemberReference newMember) {
            var result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember))));
            }
            return result;
        }
        public AggregatedParameterProvenance CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public AggregatedParameterProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            var result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType))));
            }
            return result;
        }
        public AggregatedParameterProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            var result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType))));
            }
            return result;
        }
        public AggregatedParameterProvenance? CreateEncapsulatedEnumeratorInstance() {
            var result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedEnumeratorInstance())
                        .Where(chain => chain != null)
                        .OfType<ParameterTracingChain>()));
            }
            if (result.ReferencedParameters.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithMemberAccess(member, out ParameterTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithArrayAccess(arrayType, out ParameterTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithCollectionAccess(collectionType, elementType, out ParameterTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTraceEnumeratorCurrent([NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                var newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryTraceEnumeratorCurrent(out ParameterTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, newChains);
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
