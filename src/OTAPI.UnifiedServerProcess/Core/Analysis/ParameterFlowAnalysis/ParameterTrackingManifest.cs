using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    /// <summary>
    /// Represents the tracing information of a single TrackingParameter that may exist in multiple parts
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
    /// <param name="parameter">The TrackingParameter being tracked across the object structure</param>
    /// <param name="trackingPaths">Initial collection of tracking chains for the TrackingParameter's parts</param>
    public sealed class ParameterTrackingManifest(
        ParameterDefinition parameter,
        IEnumerable<ParameterTrackingChain> trackingPaths)
    {
        /// <summary>
        /// Collection of independent tracking chains representing different access paths
        /// through which the TrackingParameter's components are referenced in the object structure.
        /// 
        /// <para>Each tail corresponds to a distinct location where the TrackingParameter's data
        /// flows in the object's encapsulation hierarchy or internal structure.</para>
        /// </summary>
        public readonly HashSet<ParameterTrackingChain> PartTrackingPaths = [.. trackingPaths];

        /// <summary>
        /// The original TrackingParameter definition being tracked across multiple locations
        /// in the object structure.
        /// </summary>
        public readonly ParameterDefinition TrackedParameter = parameter;
        public override string ToString() {
            return $"{TrackedParameter.GetDebugName()} | {string.Join(", ", PartTrackingPaths)}";
        }
    }
}
