using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    /// <summary>
    /// Represents the tracing information of a single Parameter that may exist in multiple parts
    /// within an object structure due to encapsulation or component access.
    /// 
    /// <para>Example scenario:</para>
    /// <para>ObjectInstance { 
    ///     FieldA: Parameter.FieldX, 
    ///     FieldB: { Parameter.FieldY, Parameter.FieldZ } 
    /// }</para>
    /// 
    /// <para><see cref="PartTracingPaths"/> contains independent tracing chains for each
    /// occurrence path of <see cref="TracedParameter"/> within the object structure.</para>
    /// </summary>
    /// <param name="parameter">The Parameter being traced across the object structure</param>
    /// <param name="tracingPaths">Initial collection of tracing chains for the Parameter's parts</param>
    public sealed class ParameterProvenance(
        ParameterDefinition parameter,
        IEnumerable<ParameterTracingChain> tracingPaths)
    {
        /// <summary>
        /// Collection of independent tracing chains representing different access paths
        /// through which the Parameter's components are referenced in the object structure.
        /// 
        /// <para>Each tail corresponds to a distinct location where the Parameter's data
        /// flows in the object's encapsulation hierarchy or internal structure.</para>
        /// </summary>
        public readonly HashSet<ParameterTracingChain> PartTracingPaths = [.. tracingPaths];

        /// <summary>
        /// The original Parameter definition being traced across multiple locations
        /// in the object structure.
        /// </summary>
        public readonly ParameterDefinition TracedParameter = parameter;
        public override string ToString() {
            return $"{TracedParameter.GetDebugName()} | {string.Join(", ", PartTracingPaths)}";
        }
    }
}
