using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis {
    public sealed class SingleStaticFieldTrace(FieldDefinition staticField, IEnumerable<StaticFieldOriginChain> staticFieldOrigins) {
        public readonly HashSet<StaticFieldOriginChain> StaticFieldOrigins = [.. staticFieldOrigins];
        public readonly FieldDefinition SourceStaticField = staticField;
    }
}
