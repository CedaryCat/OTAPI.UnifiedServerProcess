using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public sealed class StaticFieldProvenance(FieldDefinition staticField, IEnumerable<StaticFieldTracingChain> staticFieldOrigins)
    {
        public readonly HashSet<StaticFieldTracingChain> PartTracingPaths = [.. staticFieldOrigins];
        public readonly FieldDefinition TracingStaticField = staticField;
        public override string ToString() {
            return $"{TracingStaticField.GetIdentifier()} | {string.Join(", ", PartTracingPaths)}";
        }
    }
}
