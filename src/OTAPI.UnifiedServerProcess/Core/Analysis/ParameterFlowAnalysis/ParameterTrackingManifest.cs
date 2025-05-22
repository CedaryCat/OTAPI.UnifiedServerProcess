using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis {
    /// <summary>
    /// Represents the tracing information of a single parameter that may exist in multiple parts
    /// within an object structure due to encapsulation or component access.
    /// 
    /// <para>Example scenario:</para>
    /// <para>ObjectInstance { 
    ///     FieldA: Parameter.FieldX, 
    ///     FieldB: { Parameter.FieldY, Parameter.FieldZ } 
    /// }</para>
    /// 
    /// <para><see cref="PartTrackingPaths"/> contains independent tracking chains for each
    /// occurrence path of <see cref="TrackedParameter"/> within the object structure.</para>
    /// </summary>
    /// <param name="parameter">The parameter being tracked across the object structure</param>
    /// <param name="trackingPaths">Initial collection of tracking chains for the parameter's parts</param>
    public sealed class ParameterTrackingManifest(
        ParameterDefinition parameter,
        IEnumerable<ParameterTrackingChain> trackingPaths)
    {
        /// <summary>
        /// Collection of independent tracking chains representing different access paths
        /// through which the parameter's components are referenced in the object structure.
        /// 
        /// <para>Each chain corresponds to a distinct location where the parameter's data
        /// flows in the object's encapsulation hierarchy or internal structure.</para>
        /// </summary>
        public readonly HashSet<ParameterTrackingChain> PartTrackingPaths = [.. trackingPaths];

        /// <summary>
        /// The original parameter definition being tracked across multiple locations
        /// in the object structure.
        /// </summary>
        public readonly ParameterDefinition TrackedParameter = parameter;
    }
}
