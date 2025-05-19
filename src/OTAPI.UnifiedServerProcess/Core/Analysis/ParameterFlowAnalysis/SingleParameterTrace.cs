using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    public sealed class SingleParameterTrace(ParameterDefinition parameter, IEnumerable<ParameterOriginChain> parameterOrigins) {
        public readonly HashSet<ParameterOriginChain> ParameterOrigins = [.. parameterOrigins];
        public readonly ParameterDefinition SourceParameter = parameter;
    }
}
