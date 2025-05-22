using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class ParameterTraceCollection<TKey> where TKey : notnull {
        private readonly Dictionary<TKey, CompositeParameterTracking> _traces = [];

        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out CompositeParameterTracking? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, CompositeParameterTracking newTrace) {
            if (!_traces.TryGetValue(key, out CompositeParameterTracking? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (var originGroup in newTrace.ReferencedParameters) {
                if (!existingTrace.ReferencedParameters.TryGetValue(originGroup.Key, out var existingChains)) {
                    existingTrace.ReferencedParameters[originGroup.Key] = new ParameterTrackingManifest(originGroup.Value.TrackedParameter, originGroup.Value.PartTrackingPaths);
                    modified = true;
                    continue;
                }

                foreach (var chain in originGroup.Value.PartTrackingPaths) {
                    if (existingChains.PartTrackingPaths.Add(chain)) {
                        modified = true;
                    }
                }
            }
            return modified;
        }

        public bool TryAddOriginChain(TKey key, ParameterTrackingChain chain) {
            if (!_traces.TryGetValue(key, out CompositeParameterTracking? trace)) {
                trace = new CompositeParameterTracking();
                _traces[key] = trace;
            }

            if (!trace.ReferencedParameters.TryGetValue(chain.TrackingParameter.Name, out var singleParameter)) {
                singleParameter = new(chain.TrackingParameter, []);
                trace.ReferencedParameters[chain.TrackingParameter.Name] = singleParameter;
            }

            return singleParameter.PartTrackingPaths.Add(chain);
        }

        public IEnumerator<CompositeParameterTracking> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;
    }
}
