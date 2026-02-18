using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
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
            return CreateEncapsulatedInstance(newMember, null);
        }
        public AggregatedParameterProvenance CreateEncapsulatedInstance(MemberReference newMember, TypeFlowSccIndex? sccIndex) {
            AggregatedParameterProvenance result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember, sccIndex))));
            }
            return result;
        }
        public AggregatedParameterProvenance CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public AggregatedParameterProvenance CreateEncapsulatedInstance(FieldReference newMember, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance((MemberReference)newMember, sccIndex);
        public AggregatedParameterProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            return CreateEncapsulatedArrayInstance(arrayType, null);
        }
        public AggregatedParameterProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType, TypeFlowSccIndex? sccIndex) {
            AggregatedParameterProvenance result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType, sccIndex))));
            }
            return result;
        }
        public AggregatedParameterProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            return CreateEncapsulatedCollectionInstance(collectionType, elementType, null);
        }
        public AggregatedParameterProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex) {
            AggregatedParameterProvenance result = new AggregatedParameterProvenance();
            foreach (var origin in ReferencedParameters) {
                result.ReferencedParameters.Add(
                    origin.Key,
                    new ParameterProvenance(
                        origin.Value.TracedParameter,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType, sccIndex))));
            }
            return result;
        }
        public AggregatedParameterProvenance? CreateEncapsulatedEnumeratorInstance() {
            AggregatedParameterProvenance result = new AggregatedParameterProvenance();
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

        // Compatibility shim: legacy extend APIs default to read semantics.
        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, null, out resultTrace);
        public bool TryExtendTracingWithMemberAccess(MemberReference member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, sccIndex, out resultTrace);
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyArrayAccess(arrayType, MemberAccessOperation.Read, null, out resultTrace);
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyArrayAccess(arrayType, MemberAccessOperation.Read, sccIndex, out resultTrace);
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyCollectionAccess(collectionType, elementType, MemberAccessOperation.Read, null, out resultTrace);
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyCollectionAccess(collectionType, elementType, MemberAccessOperation.Read, sccIndex, out resultTrace);

        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyMemberAccess(member, operation, null, out resultTrace);
        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                HashSet<ParameterTracingChain> newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryApplyMemberAccess(member, operation, sccIndex, out ParameterTracingChain? newChain)) {
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
        public bool TryApplyArrayAccess(ArrayType arrayType, MemberAccessOperation operation, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyArrayAccess(arrayType, operation, null, out resultTrace);
        public bool TryApplyArrayAccess(ArrayType arrayType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                HashSet<ParameterTracingChain> newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryApplyArrayAccess(arrayType, operation, sccIndex, out ParameterTracingChain? newChain)) {
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
        public bool TryApplyCollectionAccess(TypeReference collectionType, TypeReference elementType, MemberAccessOperation operation, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace)
            => TryApplyCollectionAccess(collectionType, elementType, operation, null, out resultTrace);
        public bool TryApplyCollectionAccess(TypeReference collectionType, TypeReference elementType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedParameterProvenance? resultTrace) {
            resultTrace = new AggregatedParameterProvenance();
            bool foundAny = false;

            foreach (var originGroup in ReferencedParameters) {
                HashSet<ParameterTracingChain> newChains = new HashSet<ParameterTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryApplyCollectionAccess(collectionType, elementType, operation, sccIndex, out ParameterTracingChain? newChain)) {
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
                HashSet<ParameterTracingChain> newChains = new HashSet<ParameterTracingChain>();

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
