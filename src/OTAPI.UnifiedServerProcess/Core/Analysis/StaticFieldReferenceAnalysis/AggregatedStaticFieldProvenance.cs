using Mono.Cecil;
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
            var result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedInstance(newMember))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedInstance(FieldReference newMember)
            => CreateEncapsulatedInstance((MemberReference)newMember);
        public AggregatedStaticFieldProvenance CreateEncapsulatedArrayInstance(ArrayType arrayType) {
            var result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedArrayInstance(arrayType))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType) {
            var result = new AggregatedStaticFieldProvenance();
            foreach (var origin in TracedStaticFields) {
                result.TracedStaticFields.Add(
                    origin.Key,
                    new StaticFieldProvenance(
                        origin.Value.TracingStaticField,
                        origin.Value.PartTracingPaths.Select(chain => chain.CreateEncapsulatedCollectionInstance(collectionType, elementType))));
            }
            return result;
        }
        public AggregatedStaticFieldProvenance? CreateEncapsulatedEnumeratorInstance() {
            var result = new AggregatedStaticFieldProvenance();
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

        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                var newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithMemberAccess(member, out StaticFieldTracingChain? newChain)) {
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
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                var newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithArrayAccess(arrayType, out StaticFieldTracingChain? newChain)) {
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
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? resultTrace) {
            resultTrace = new AggregatedStaticFieldProvenance();
            bool foundAny = false;

            foreach (var originGroup in TracedStaticFields) {
                var newChains = new HashSet<StaticFieldTracingChain>();

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (chain.TryExtendTracingWithCollectionAccess(collectionType, elementType, out StaticFieldTracingChain? newChain)) {
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
                var newChains = new HashSet<StaticFieldTracingChain>();

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
