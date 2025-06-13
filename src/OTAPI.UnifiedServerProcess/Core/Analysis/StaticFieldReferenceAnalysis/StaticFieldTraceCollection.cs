using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class StaticFieldTraceCollection<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, CompositeStaticFieldTracking> _traces = [];
        public bool TryGetTrace(TKey key, [NotNullWhen(true)] out CompositeStaticFieldTracking? trace) =>
            _traces.TryGetValue(key, out trace);

        public bool TryAddTrace(TKey key, CompositeStaticFieldTracking newTrace) {
            if (!_traces.TryGetValue(key, out CompositeStaticFieldTracking? existingTrace)) {
                _traces[key] = newTrace;
                return true;
            }

            bool modified = false;
            foreach (var originGroup in newTrace.TrackedStaticField) {
                if (!existingTrace.TrackedStaticField.TryGetValue(originGroup.Key, out var existingChains)) {
                    existingTrace.TrackedStaticField[originGroup.Key] = new SingleStaticFieldTrace(originGroup.Value.TrackingStaticField, originGroup.Value.PartTrackingPaths);
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

        public bool TryAddOriginChain(TKey key, StaticFieldTrackingChain chain) {
            if (!_traces.TryGetValue(key, out CompositeStaticFieldTracking? trace)) {
                trace = new CompositeStaticFieldTracking();
                _traces[key] = trace;
            }

            if (!trace.TrackedStaticField.TryGetValue(chain.TrackingStaticField.Name, out var singleStaticField)) {
                singleStaticField = new(chain.TrackingStaticField, []);
                trace.TrackedStaticField[chain.TrackingStaticField.Name] = singleStaticField;
            }

            return singleStaticField.PartTrackingPaths.Add(chain);
        }

        public IEnumerator<CompositeStaticFieldTracking> GetEnumerator() => _traces.Values.GetEnumerator();
        public int Count => _traces.Count;
    }
}
