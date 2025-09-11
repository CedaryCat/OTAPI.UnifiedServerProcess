using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class StaticFieldTraceCollection<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, AggregatedStaticFieldProvenance> _traces = [];
        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out AggregatedStaticFieldProvenance? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, AggregatedStaticFieldProvenance newTrace) {
            if (!_traces.TryGetValue(key, out AggregatedStaticFieldProvenance? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (var originGroup in newTrace.TracedStaticFields) {
                if (!existingTrace.TracedStaticFields.TryGetValue(originGroup.Key, out var existingChains)) {
                    existingTrace.TracedStaticFields[originGroup.Key] = new StaticFieldProvenance(originGroup.Value.TracingStaticField, originGroup.Value.PartTracingPaths);
                    modified = true;
                    continue;
                }

                foreach (var chain in originGroup.Value.PartTracingPaths) {
                    if (existingChains.PartTracingPaths.Add(chain)) {
                        modified = true;
                    }
                }
            }
            return modified;
        }

        public bool TryAddOriginChain(TKey key, StaticFieldTracingChain chain) {
            if (!_traces.TryGetValue(key, out AggregatedStaticFieldProvenance? trace)) {
                trace = new AggregatedStaticFieldProvenance();
                _traces[key] = trace;
            }

            if (!trace.TracedStaticFields.TryGetValue(chain.TracingStaticField.GetIdentifier(), out var singleStaticField)) {
                singleStaticField = new(chain.TracingStaticField, []);
                trace.TracedStaticFields[chain.TracingStaticField.GetIdentifier()] = singleStaticField;
            }

            return singleStaticField.PartTracingPaths.Add(chain);
        }

        public IEnumerator<AggregatedStaticFieldProvenance> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;
    }
}
