using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public sealed class SingleStaticFieldTrace(FieldDefinition staticField, IEnumerable<StaticFieldTrackingChain> staticFieldOrigins)
    {
        public readonly HashSet<StaticFieldTrackingChain> PartTrackingPaths = [.. staticFieldOrigins];
        public readonly FieldDefinition TrackingStaticField = staticField;
        public override string ToString() {
            return $"{TrackingStaticField.GetIdentifier()} | {string.Join(", ", PartTrackingPaths)}";
        }
    }
}
