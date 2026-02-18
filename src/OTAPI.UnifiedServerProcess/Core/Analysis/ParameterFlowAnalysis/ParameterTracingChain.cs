using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    public sealed class ParameterTracingChain : IEquatable<ParameterTracingChain>
    {
        /// <summary>
        /// The original Parameter definition being traced
        /// </summary>

        public readonly ParameterDefinition TracingParameter;
        /// <summary>
        /// When a Parameter is encapsulated within an instance, the storeIn containing the Parameter
        /// is prepended to this hierarchy in the tail replica created by <see cref="CreateEncapsulatedInstance"/>.
        /// Subsequent encapsulations will continue prepending members. This hierarchy represents the path
        /// from outermost container to inner values needed to access the traced Parameter.
        /// 
        /// <para>When accessing members from traced objects via methods like <see cref="TryExtendTracingWithMemberAccess"/>:</para>
        /// <para>1. If accessed storeIn matches the first element in the hierarchy, it indicates unwrapping - returns true</para>
        /// <para>2. If storeIn doesn't match hierarchy head - indicates unrelated access - returns false</para>
        /// <para>3. Empty hierarchy means tracing base Parameter. Member accesses are considered part of
        /// the Parameter itself and extend <see cref="ComponentAccessPath"/> instead - returns true</para>
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> EncapsulationHierarchy = [];
        /// <summary>
        /// When directly accessing members of the base Parameter (when <see cref="EncapsulationHierarchy"/> is empty),
        /// these accesses are recorded here. Unlike the encapsulation hierarchy, this tail represents the internal
        /// structure of the Parameter itself and doesn't support unwrapping semantics.
        /// </summary>
        public readonly ImmutableArray<MemberAccessStep> ComponentAccessPath = [];
        readonly string Key;

        public ParameterTracingChain(ParameterDefinition sourceParameter, IEnumerable<MemberAccessStep> encapsulationHierarchy, IEnumerable<MemberAccessStep> componentAccessPath) {
            TracingParameter = sourceParameter ?? throw new ArgumentNullException(nameof(sourceParameter));
            EncapsulationHierarchy = encapsulationHierarchy?.ToImmutableArray() ?? [];
            ComponentAccessPath = componentAccessPath?.ToImmutableArray() ?? [];
            Key = ToString();
        }
        private ParameterTracingChain(ParameterTracingChain baseChain, MemberAccessStep encapsulationLayer) {
            TracingParameter = baseChain.TracingParameter;
            EncapsulationHierarchy = baseChain.EncapsulationHierarchy.Insert(0, encapsulationLayer);
            ComponentAccessPath = baseChain.ComponentAccessPath;
            Key = ToString();
        }
        public ParameterTracingChain CreateEncapsulatedArrayInstance(ArrayType arrayType)
            => CreateEncapsulatedInstance(new ArrayElementLayer(arrayType), null);
        public ParameterTracingChain CreateEncapsulatedArrayInstance(ArrayType arrayType, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance(new ArrayElementLayer(arrayType), sccIndex);
        public ParameterTracingChain CreateEncapsulatedInstance(MemberReference storeIn)
            => CreateEncapsulatedInstance((MemberAccessStep)storeIn, null);
        public ParameterTracingChain CreateEncapsulatedInstance(MemberReference storeIn, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance((MemberAccessStep)storeIn, sccIndex);
        public ParameterTracingChain CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType)
            => CreateEncapsulatedInstance(new CollectionElementLayer(collectionType, elementType), null);
        public ParameterTracingChain CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance(new CollectionElementLayer(collectionType, elementType), sccIndex);

        private ParameterTracingChain CreateEncapsulatedInstance(MemberAccessStep storedIn, TypeFlowSccIndex? sccIndex) {
            if (sccIndex is null) {
                return new ParameterTracingChain(this, storedIn);
            }

            ImmutableArray<MemberAccessStep> newHierarchy = EncapsulationHierarchy.Insert(0, storedIn);
            ImmutableArray<MemberAccessStep> normalizedHierarchy = NormalizeEncapsulationHierarchyWithSccLoops(newHierarchy, sccIndex);

            return new ParameterTracingChain(TracingParameter, normalizedHierarchy, ComponentAccessPath);
        }

        private static ImmutableArray<MemberAccessStep> NormalizeEncapsulationHierarchyWithSccLoops(
            ImmutableArray<MemberAccessStep> hierarchy,
            TypeFlowSccIndex sccIndex) {

            if (hierarchy.IsEmpty) {
                return hierarchy;
            }

            ImmutableArray<MemberAccessStep>.Builder builder = ImmutableArray.CreateBuilder<MemberAccessStep>(hierarchy.Length);

            int? activeSccId = null;
            bool hasLoopSummary = false;
            HashSet<string>? seen = null;

            foreach (MemberAccessStep step in hierarchy) {
                if (step is SccLoopLayer loop) {
                    activeSccId = loop.SccId;
                    hasLoopSummary = true;
                    seen = null;

                    if (builder.Count == 0 || builder[^1] is not SccLoopLayer prev || prev.SccId != loop.SccId) {
                        builder.Add(loop);
                    }

                    continue;
                }

                if (!sccIndex.TryGetRecursiveSccIdIncludingBaseTypes(step.DeclaringType, out var sccId)
                    || !sccIndex.IsRecursiveScc(sccId)
                    || !sccIndex.IsInSccIncludingBaseTypes(step.MemberType, sccId)) {

                    activeSccId = null;
                    hasLoopSummary = false;
                    seen = null;
                    builder.Add(step);
                    continue;
                }

                if (activeSccId != sccId) {
                    activeSccId = sccId;
                    hasLoopSummary = false;
                    seen = new HashSet<string>(StringComparer.Ordinal) { step.DeclaringType.FullName };

                    builder.Add(step);

                    if (!seen.Add(step.MemberType.FullName)) {
                        builder.Add(new SccLoopLayer(sccId, step.DeclaringType));
                        hasLoopSummary = true;
                        seen = null;
                    }

                    continue;
                }

                if (hasLoopSummary) {
                    continue;
                }

                builder.Add(step);

                seen ??= new HashSet<string>(StringComparer.Ordinal);
                if (seen.Count == 0) {
                    seen.Add(step.DeclaringType.FullName);
                }

                if (!seen.Add(step.MemberType.FullName)) {
                    builder.Add(new SccLoopLayer(sccId, step.DeclaringType));
                    hasLoopSummary = true;
                    seen = null;
                }
            }

            return builder.ToImmutable();
        }
        public ParameterTracingChain? CreateEncapsulatedEnumeratorInstance() {
            if (EncapsulationHierarchy.IsEmpty) {
                return null;
            }
            if (EncapsulationHierarchy[0] is ArrayElementLayer or CollectionElementLayer) {
                MemberAccessStep collectionEle = EncapsulationHierarchy[0];
                return new ParameterTracingChain(this, new EnumeratorLayer(collectionEle.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (EncapsulationHierarchy[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }
        public override string ToString() {
            var paramName = TracingParameter.GetDebugName();
            if (!EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${paramName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else if (!EncapsulationHierarchy.IsEmpty && ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${paramName} }}";
            }
            else if (EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ ${paramName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else {
                return $"{{ ${paramName} }}";
            }
        }

        // Compatibility shim: legacy extend APIs default to read semantics.
        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, null, out result);
        public bool TryExtendTracingWithMemberAccess(MemberReference member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, sccIndex, out result);
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyArrayAccess(arrayType, MemberAccessOperation.Read, sccIndex, out result);
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyCollectionAccess(collectionType, elementType, MemberAccessOperation.Read, sccIndex, out result);

        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyMemberAccess((MemberAccessStep)member, operation, null, out result);
        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result)
            => TryApplyMemberAccess((MemberAccessStep)member, operation, sccIndex, out result);
        public bool TryApplyArrayAccess(ArrayType arrayType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result) {
            var indexer = new ArrayElementLayer(arrayType);
            return TryApplyMemberAccess(indexer, operation, sccIndex, out result);
        }
        public bool TryApplyCollectionAccess(TypeReference collectionType, TypeReference elementType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryApplyMemberAccess(collection, operation, sccIndex, out result);
        }
        private bool TryApplyMemberAccess(MemberAccessStep member, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result) {
            result = null;

            if (EncapsulationHierarchy.IsEmpty) {
                if (operation is MemberAccessOperation.GetAddress or MemberAccessOperation.Write) {
                    return TryExtendComponentAccessPath(member, sccIndex, out result);
                }

                bool isValidReference;

                if (member is RealMemberLayer realMember) {
                    isValidReference = realMember.Member switch {
                        MethodReference m => !m.ReturnType.IsTruelyValueType(),
                        FieldReference f => !f.FieldType.IsTruelyValueType(),
                        PropertyReference p => !p.PropertyType.IsTruelyValueType(),
                        _ => false
                    };
                }
                else if (member is ArrayElementLayer indexer) {
                    isValidReference = !indexer.MemberType.IsTruelyValueType();
                }
                else if (member is CollectionElementLayer collection) {
                    isValidReference = !collection.MemberType.IsTruelyValueType();
                }
                else {
                    isValidReference = false;
                }

                if (isValidReference) {
                    return TryExtendComponentAccessPath(member, sccIndex, out result);
                }
                return false;
            }

            if (EncapsulationHierarchy[0] is SccLoopLayer loopLayer) {
                if (sccIndex is null || !sccIndex.IsRecursiveScc(loopLayer.SccId)) {
                    return false;
                }

                if (!sccIndex.IsInSccIncludingBaseTypes(member.DeclaringType, loopLayer.SccId)) {
                    return false;
                }

                // Access within the SCC doesn't narrow the path.
                if (member is ArrayElementLayer or CollectionElementLayer) {
                    if (sccIndex.IsInSccIncludingBaseTypes(member.MemberType, loopLayer.SccId)) {
                        result = this;
                        return true;
                    }
                }
                else if (sccIndex.IsInSccIncludingBaseTypes(member.MemberType, loopLayer.SccId)) {
                    result = this;
                    return true;
                }

                // Exiting the SCC must match the next concrete layer (if any).
                if (EncapsulationHierarchy.Length >= 2 && member.IsSameLayer(EncapsulationHierarchy[1])) {
                    result = new ParameterTracingChain(
                        TracingParameter,
                        EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                        ComponentAccessPath
                    );
                    return true;
                }

                return false;
            }

            if (member.IsSameLayer(EncapsulationHierarchy[0])) {
                result = new ParameterTracingChain(
                    TracingParameter,
                    EncapsulationHierarchy.RemoveAt(0),
                    ComponentAccessPath
                );
                return true;
            }

            return false;
        }

        private enum ComponentStateKind { ExactType, InScc }
        private readonly struct ComponentPathState
        {
            public ComponentStateKind Kind { get; }
            public TypeReference? ExactType { get; }
            public int SccId { get; }

            private ComponentPathState(ComponentStateKind kind, TypeReference? exactType, int sccId) {
                Kind = kind;
                ExactType = exactType;
                SccId = sccId;
            }

            public static ComponentPathState Exact(TypeReference type) => new(ComponentStateKind.ExactType, type, -1);
            public static ComponentPathState InScc(int sccId) => new(ComponentStateKind.InScc, null, sccId);
        }

        private ComponentPathState ComputeComponentPathState(TypeFlowSccIndex sccIndex) {
            var state = ComponentPathState.Exact(TracingParameter.ParameterType);

            foreach (MemberAccessStep step in ComponentAccessPath) {
                if (step is SccLoopLayer loop) {
                    state = ComponentPathState.InScc(loop.SccId);
                    continue;
                }

                if (state.Kind == ComponentStateKind.ExactType) {
                    state = ComponentPathState.Exact(step.MemberType);
                    continue;
                }

                if (step is ArrayElementLayer or CollectionElementLayer) {
                    continue;
                }

                if (sccIndex.IsInSccIncludingBaseTypes(step.DeclaringType, state.SccId) && !sccIndex.IsInSccIncludingBaseTypes(step.MemberType, state.SccId)) {
                    state = ComponentPathState.Exact(step.MemberType);
                }
            }

            return state;
        }

        private bool TryExtendComponentAccessPath(MemberAccessStep member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out ParameterTracingChain? result) {
            if (sccIndex is null) {
                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
                return true;
            }

            if (member is SccLoopLayer loop) {
                if (!sccIndex.IsRecursiveScc(loop.SccId)) {
                    result = null;
                    return false;
                }

                ComponentPathState loopState = ComputeComponentPathState(sccIndex);

                if (loopState.Kind == ComponentStateKind.InScc) {
                    result = this;
                    return true;
                }

                TypeReference loopBaseType = loopState.ExactType ?? TracingParameter.ParameterType;
                if (!sccIndex.IsInSccIncludingBaseTypes(loopBaseType, loop.SccId)) {
                    result = this;
                    return true;
                }

                if (!ComponentAccessPath.IsEmpty
                    && ComponentAccessPath[^1] is SccLoopLayer prev
                    && prev.SccId == loop.SccId) {

                    result = this;
                    return true;
                }

                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, loop]);
                return true;
            }

            ComponentPathState currentState = ComputeComponentPathState(sccIndex);

            // If we are currently "inside" a recursive SCC (unknown node), only allow exit edges.
            if (currentState.Kind == ComponentStateKind.InScc) {
                var activeSccId = currentState.SccId;
                if (!sccIndex.IsInSccIncludingBaseTypes(member.DeclaringType, activeSccId)) {
                    result = null;
                    return false;
                }
                // Element access inside an SCC can blow up (.{Element}.{Element}...) when generic typing is imprecise.
                // Treat element access as always internal while inside an SCC; exits should be expressed via a real member edge.
                if (member is ArrayElementLayer or CollectionElementLayer) {
                    result = this;
                    return true;
                }
                if (sccIndex.IsInSccIncludingBaseTypes(member.MemberType, activeSccId)) {
                    result = this;
                    return true;
                }
                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
                return true;
            }

            TypeReference baseType = currentState.ExactType ?? TracingParameter.ParameterType;
            if (!sccIndex.TryGetRecursiveSccIdIncludingBaseTypes(baseType, out var sccId) || !sccIndex.IsRecursiveScc(sccId)) {
                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
                return true;
            }

            if (!sccIndex.IsInSccIncludingBaseTypes(member.MemberType, sccId)) {
                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
                return true;
            }

            // Insert a loop summary once we detect a repeated type within the SCC.
            if (WouldRevisitTypeWithinScc(TracingParameter.ParameterType, ComponentAccessPath, member, sccIndex, sccId)) {
                result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member, new SccLoopLayer(sccId, baseType)]);
                return true;
            }

            result = new ParameterTracingChain(TracingParameter, [], [.. ComponentAccessPath, member]);
            return true;
        }

        private static bool WouldRevisitTypeWithinScc(
            TypeReference startType,
            ImmutableArray<MemberAccessStep> existingPath,
            MemberAccessStep nextStep,
            TypeFlowSccIndex sccIndex,
            int sccId) {

            var seen = new HashSet<string>(StringComparer.Ordinal);
            TypeReference current = startType;

            bool inScc = sccIndex.IsInSccIncludingBaseTypes(current, sccId);
            if (inScc) {
                seen.Add(current.FullName);
            }

            foreach (MemberAccessStep step in existingPath) {
                if (step is SccLoopLayer loop && loop.SccId == sccId) {
                    // Previous loop summaries shouldn't prevent detecting a new cycle after exiting and re-entering the SCC.
                    seen.Clear();
                    inScc = true;
                    continue;
                }

                current = step.MemberType;
                inScc = sccIndex.IsInSccIncludingBaseTypes(current, sccId);

                if (!inScc) {
                    seen.Clear();
                    continue;
                }

                seen.Add(current.FullName);
            }

            TypeReference nextType = nextStep.MemberType;
            return sccIndex.IsInSccIncludingBaseTypes(nextType, sccId) && seen.Contains(nextType.FullName);
        }
        public bool TryTraceEnumeratorCurrent([NotNullWhen(true)] out ParameterTracingChain? result) {
            if (EncapsulationHierarchy.Length < 2) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[0] is not EnumeratorLayer) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[1] is not CollectionElementLayer) {
                throw new NotSupportedException("Enumerator layer must be followed by ArrayElementLayer or CollectionElementLayer.");
            }
            result = new ParameterTracingChain(
                TracingParameter,
                EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                ComponentAccessPath
            );
            return true;
        }
        public static ParameterTracingChain? CombineParameterTraces(ParameterTracingChain outerPart, ParameterTracingChain innerPart)
            => CombineParameterTraces(outerPart, innerPart, null);

        public static ParameterTracingChain? CombineParameterTraces(ParameterTracingChain outerPart, ParameterTracingChain innerPart, TypeFlowSccIndex? sccIndex) {
            if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                if (!TryMatchPrefixWithSccLoops(innerPart.ComponentAccessPath, outerPart.EncapsulationHierarchy, sccIndex, out var consumedInner, out var consumedOuter)) {
                    return null;
                }

                ImmutableArray<MemberAccessStep> combinedEncapsulation = innerPart.EncapsulationHierarchy.AddRange(outerPart.EncapsulationHierarchy.Skip(consumedOuter));
                if (sccIndex is not null) {
                    combinedEncapsulation = NormalizeEncapsulationHierarchyWithSccLoops(combinedEncapsulation, sccIndex);
                }

                return new ParameterTracingChain(
                    outerPart.TracingParameter,
                    combinedEncapsulation,
                    innerPart.ComponentAccessPath.Skip(consumedInner)
                );
            }
            else if (innerPart.ComponentAccessPath.Length == 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                ImmutableArray<MemberAccessStep> combinedEncapsulation = innerPart.EncapsulationHierarchy.AddRange(outerPart.EncapsulationHierarchy);
                if (sccIndex is not null) {
                    combinedEncapsulation = NormalizeEncapsulationHierarchyWithSccLoops(combinedEncapsulation, sccIndex);
                }

                return new ParameterTracingChain(outerPart.TracingParameter,
                    combinedEncapsulation,
                    outerPart.ComponentAccessPath
                );
            }
            else if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length == 0) {
                if (sccIndex is null) {
                    return new ParameterTracingChain(outerPart.TracingParameter,
                        [],
                        [.. outerPart.ComponentAccessPath, .. innerPart.ComponentAccessPath]
                    );
                }

                var combined = new ParameterTracingChain(outerPart.TracingParameter, [], outerPart.ComponentAccessPath);
                foreach (MemberAccessStep step in innerPart.ComponentAccessPath) {
                    if (!combined.TryExtendComponentAccessPath(step, sccIndex, out ParameterTracingChain? next)) {
                        return null;
                    }
                    combined = next;
                }

                return combined;
            }
            else {
                return new ParameterTracingChain(outerPart.TracingParameter,
                    [],
                    []
                );
            }
        }

        private static bool TryMatchPrefixWithSccLoops(
            ImmutableArray<MemberAccessStep> innerComponent,
            ImmutableArray<MemberAccessStep> outerEncapsulation,
            TypeFlowSccIndex? sccIndex,
            out int consumedInner,
            out int consumedOuter) {

            int i = 0;
            int j = 0;

            while (i < innerComponent.Length) {
                while (j < outerEncapsulation.Length && outerEncapsulation[j] is SccLoopLayer outerLoop) {
                    if (sccIndex is null || !sccIndex.IsRecursiveScc(outerLoop.SccId)) {
                        consumedInner = 0;
                        consumedOuter = 0;
                        return false;
                    }
                    j++;
                }

                MemberAccessStep step = innerComponent[i];

                if (step is SccLoopLayer loop) {
                    if (sccIndex is null || !sccIndex.IsRecursiveScc(loop.SccId)) {
                        consumedInner = 0;
                        consumedOuter = 0;
                        return false;
                    }

                    while (j < outerEncapsulation.Length && sccIndex.IsInSccIncludingBaseTypes(outerEncapsulation[j].DeclaringType, loop.SccId)) {
                        j++;
                    }

                    i++;
                    continue;
                }

                if (j >= outerEncapsulation.Length) {
                    break;
                }

                if (!step.IsSameLayer(outerEncapsulation[j])) {
                    consumedInner = 0;
                    consumedOuter = 0;
                    return false;
                }

                i++;
                j++;
            }

            consumedInner = i;
            consumedOuter = j;
            return true;
        }

        private static ImmutableArray<MemberAccessStep> ConcatWithoutOverlappingPrefix(
            ImmutableArray<MemberAccessStep> left,
            ImmutableArray<MemberAccessStep> right) {

            if (left.IsEmpty) return right;
            if (right.IsEmpty) return left;

            var maxOverlap = Math.Min(left.Length, right.Length);

            for (int overlap = maxOverlap; overlap >= 1; overlap--) {
                bool matches = true;
                for (int i = 0; i < overlap; i++) {
                    if (!left[left.Length - overlap + i].IsSameLayer(right[i])) {
                        matches = false;
                        break;
                    }
                }
                if (matches) {
                    return [.. left, .. right.Skip(overlap)];
                }
            }

            return [.. left, .. right];
        }
        public override bool Equals(object? obj) => Equals(obj as ParameterTracingChain);
        public bool Equals(ParameterTracingChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
