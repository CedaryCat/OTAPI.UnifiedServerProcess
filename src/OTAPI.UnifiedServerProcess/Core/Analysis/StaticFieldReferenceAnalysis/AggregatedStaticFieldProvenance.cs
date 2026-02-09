using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class AggregatedStaticFieldProvenance
    {
        public readonly Dictionary<string, StaticFieldProvenance> TracedStaticFields = [];

        public AggregatedStaticFieldProvenance() { }
        public AggregatedStaticFieldProvenance CreateEncapsulatedInstance(MemberReference newMember) {
            return CreateEncapsulatedInstance(newMember, null);
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedInstance(MemberReference newMember, TypeFlowSccIndex? sccIndex) {
            AggregatedStaticFieldProvenance result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember, sccIndex))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public AggregatedStaticFieldProvenance CreateEncapsulatedInstance(FieldReference newMember, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance((MemberReference)newMember, sccIndex);
        public AggregatedStaticFieldProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            return CreateEncapsulatedArrayInstance(arrayType, null);
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType, TypeFlowSccIndex? sccIndex) {
            AggregatedStaticFieldProvenance result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType, sccIndex))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            return CreateEncapsulatedCollectionInstance(collectionType, elementType, null);
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex) {
            AggregatedStaticFieldProvenance result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType, sccIndex))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance? CreateEncapsulatedEnumeratorInstance() {
            AggregatedStaticFieldProvenance result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedEnumeratorInstance())
                        .Where(chain => chain != null)
                        .OfType<StaticFieldTracingChain>()));
            }
            if (result.TracedStaticFields.Count == 0) {
                return null;
            }
            return result;
        }

        public bool TryExtendTracingWithMemberAccess(MemberReference member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                HashSet<StaticFieldTracingChain> newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithMemberAccess(member, sccIndex, out StaticFieldTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TracedStaticFields[originGroup.Key] = new StaticFieldProvenance(originGroup.Value.TracingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }

        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                HashSet<StaticFieldTracingChain> newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithArrayAccess(arrayType, sccIndex, out StaticFieldTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TracedStaticFields[originGroup.Key] = new StaticFieldProvenance(originGroup.Value.TracingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }

        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                HashSet<StaticFieldTracingChain> newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithCollectionAccess(collectionType, elementType, sccIndex, out StaticFieldTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TracedStaticFields[originGroup.Key] = new StaticFieldProvenance(originGroup.Value.TracingStaticField, newChains);
                }
            }

            if (!foundAny) {
                resultTrace = null;
                return false;
            }
            return true;
        }
        public bool TryTraceEnumeratorCurrent([NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                HashSet<StaticFieldTracingChain> newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryTraceEnumeratorCurrent(out StaticFieldTracingChain? newChain)) {
                        newChains.Add(newChain);
                        foundAny = true;
                    }
                }

                if (newChains.Count > 0) {
                    resultTrace.TracedStaticFields[originGroup.Key] = new StaticFieldProvenance(originGroup.Value.TracingStaticField, newChains);
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
