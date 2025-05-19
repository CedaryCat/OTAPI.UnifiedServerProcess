using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis {
    public class StaticFieldTraceCollection<TKey> where TKey : notnull {
        private readonly Dictionary<TKey, CompositeStaticFieldTrace> _traces = [];
        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out CompositeStaticFieldTrace? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, CompositeStaticFieldTrace newTrace) {
            if (!_traces.TryGetValue(key, out CompositeStaticFieldTrace? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (var originGroup in newTrace.StaticFieldOrigins) {
                if (!existingTrace.StaticFieldOrigins.TryGetValue(originGroup.Key, out var existingChains)) {
                    existingTrace.StaticFieldOrigins[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.SourceStaticField, originGroup.Value.StaticFieldOrigins);
                    modified = true;
                    continue;
                }

                foreach (var chain in originGroup.Value.StaticFieldOrigins) {
                    if (existingChains.StaticFieldOrigins.Add(chain)) {
                        modified = true;
                    }
                }
            }
            return modified;
        }

        public bool TryAddOriginChain(TKey key, StaticFieldOriginChain chain) {
            if (!_traces.TryGetValue(key, out CompositeStaticFieldTrace? trace)) {
                trace = new CompositeStaticFieldTrace();
                _traces[key] = trace;
            }

            if (!trace.StaticFieldOrigins.TryGetValue(chain.SourceStaticField.Name, out var singleStaticField)) {
                singleStaticField = new(chain.SourceStaticField, []);
                trace.StaticFieldOrigins[chain.SourceStaticField.Name] = singleStaticField;
            }

            return singleStaticField.StaticFieldOrigins.Add(chain);
        }

        public IEnumerator<CompositeStaticFieldTrace> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;
    }
}
