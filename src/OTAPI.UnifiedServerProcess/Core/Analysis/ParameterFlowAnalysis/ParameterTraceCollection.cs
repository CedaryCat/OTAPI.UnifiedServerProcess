using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    public sealed class ParameterTraceCollection<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, AggregatedParameterProvenance> _traces = [];

        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out AggregatedParameterProvenance? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, AggregatedParameterProvenance newTrace) {
            if (!_traces.TryGetValue(key, out AggregatedParameterProvenance? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (KeyValuePair<string, ParameterProvenance> originGroup in newTrace.ReferencedParameters) {
                if (!existingTrace.ReferencedParameters.TryGetValue(originGroup.Key, out ParameterProvenance? existingChains)) {
                    existingTrace.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, originGroup.Value.PartTracingPaths);
                    modified = true;
                    continue;
                }

                foreach (ParameterTracingChain chain in originGroup.Value.PartTracingPaths) {
                    if (existingChains.PartTracingPaths.Add(chain)) {
                        modified = true;
                    }
                }
            }
            return modified;
        }

        public bool TryAddOriginChain(TKey key, ParameterTracingChain chain) {
            if (!_traces.TryGetValue(key, out AggregatedParameterProvenance? trace)) {
                trace = new AggregatedParameterProvenance();
                _traces[key] = trace;
            }

            if (!trace.ReferencedParameters.TryGetValue(chain.TracingParameter.Name, out ParameterProvenance? singleParameter)) {
                singleParameter = new(chain.TracingParameter, []);
                trace.ReferencedParameters[chain.TracingParameter.Name] = singleParameter;
            }

            return singleParameter.PartTracingPaths.Add(chain);
        }

        public IEnumerator<AggregatedParameterProvenance> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;

        public void RemapKeys(Func<TKey, TKey> remapKey) {
            ArgumentNullException.ThrowIfNull(remapKey);

            if (_traces.Count == 0) {
                return;
            }

            var remapped = new Dictionary<TKey, AggregatedParameterProvenance>(_traces.Count);

            foreach ((TKey? key, AggregatedParameterProvenance? trace) in _traces) {
                TKey newKey = remapKey(key);
                if (!remapped.TryAdd(newKey, trace)) {
                    // Merge into existing trace under the remapped key.
                    AggregatedParameterProvenance existing = remapped[newKey];
                    foreach (KeyValuePair<string, ParameterProvenance> originGroup in trace.ReferencedParameters) {
                        if (!existing.ReferencedParameters.TryGetValue(originGroup.Key, out ParameterProvenance? existingChains)) {
                            existing.ReferencedParameters[originGroup.Key] = new ParameterProvenance(originGroup.Value.TracedParameter, originGroup.Value.PartTracingPaths);
                            continue;
                        }

                        foreach (ParameterTracingChain chain in originGroup.Value.PartTracingPaths) {
                            existingChains.PartTracingPaths.Add(chain);
                        }
                    }
                }
            }

            _traces.Clear();
            foreach ((TKey? key, AggregatedParameterProvenance? trace) in remapped) {
                _traces.Add(key, trace);
            }
        }
    }
}
