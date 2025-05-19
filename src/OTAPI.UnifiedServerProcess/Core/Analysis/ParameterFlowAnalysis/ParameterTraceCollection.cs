using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class ParameterTraceCollection<TKey> where TKey : notnull {
        private readonly Dictionary<TKey, CompositeParameterTrace> _traces = [];

        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out CompositeParameterTrace? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, CompositeParameterTrace newTrace) {
            if (!_traces.TryGetValue(key, out CompositeParameterTrace? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (var originGroup in newTrace.ParameterOrigins) {
                if (!existingTrace.ParameterOrigins.TryGetValue(originGroup.Key, out var existingChains)) {
                    existingTrace.ParameterOrigins[originGroup.Key] = new SingleParameterTrace(originGroup.Value.SourceParameter, originGroup.Value.ParameterOrigins);
                    modified = true;
                    continue;
                }

                foreach (var chain in originGroup.Value.ParameterOrigins) {
                    if (existingChains.ParameterOrigins.Add(chain)) {
                        modified = true;
                    }
                }
            }
            return modified;
        }

        public bool TryAddOriginChain(TKey key, ParameterOriginChain chain) {
            if (!_traces.TryGetValue(key, out CompositeParameterTrace? trace)) {
                trace = new CompositeParameterTrace();
                _traces[key] = trace;
            }

            if (!trace.ParameterOrigins.TryGetValue(chain.SourceParameter.Name, out var singleParameter)) {
                singleParameter = new(chain.SourceParameter, []);
                trace.ParameterOrigins[chain.SourceParameter.Name] = singleParameter;
            }

            return singleParameter.ParameterOrigins.Add(chain);
        }

        public IEnumerator<CompositeParameterTrace> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;
    }
}
