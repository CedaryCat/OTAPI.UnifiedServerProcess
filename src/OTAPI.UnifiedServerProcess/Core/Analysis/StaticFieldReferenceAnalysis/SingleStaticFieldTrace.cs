using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public sealed class SingleStaticFieldTrace(FieldDefinition staticField, IEnumerable<StaticFieldTrackingChain> staticFieldOrigins)
    {
        public readonly HashSet<StaticFieldTrackingChain> PartTrackingPaths = [.. staticFieldOrigins];
        public readonly FieldDefinition TrackingStaticField = staticField;
    }
}
