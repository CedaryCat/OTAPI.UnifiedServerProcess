using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class StaticFieldTracingChain : IEquatable<StaticFieldTracingChain>
    {
        /// <summary>
        /// The static field definition being traced
        /// </summary>
        public readonly FieldDefinition TracingStaticField;
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

        public StaticFieldTracingChain(FieldDefinition tracingStataicField, IEnumerable<MemberAccessStep> encapsulationHierarchy, IEnumerable<MemberAccessStep> componentAccessPath) {
            TracingStaticField = tracingStataicField ?? throw new ArgumentNullException(nameof(tracingStataicField));
            EncapsulationHierarchy = encapsulationHierarchy?.ToImmutableArray() ?? [];
            ComponentAccessPath = componentAccessPath?.ToImmutableArray() ?? [];
            Key = ToString();
        }

        public StaticFieldTracingChain(StaticFieldTracingChain baseChain, MethodReference storedIn) : this(baseChain, (MemberAccessStep)storedIn) { }
        private StaticFieldTracingChain(StaticFieldTracingChain baseChain, MemberAccessStep storedIn) {
            TracingStaticField = baseChain.TracingStaticField;
            EncapsulationHierarchy = baseChain.EncapsulationHierarchy.Insert(0, storedIn);
            ComponentAccessPath = baseChain.ComponentAccessPath;
            Key = ToString();
        }

        public override string ToString() {
            string fieldName = TracingStaticField.Name;
            if (!EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${fieldName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else if (!EncapsulationHierarchy.IsEmpty && ComponentAccessPath.IsEmpty) {
                return $"{{ {string.Join(".", EncapsulationHierarchy.Select(m => m.Name))}: ${fieldName} }}";
            }
            else if (EncapsulationHierarchy.IsEmpty && !ComponentAccessPath.IsEmpty) {
                return $"{{ ${fieldName}.{string.Join(".", ComponentAccessPath.Select(m => m.Name))} }}";
            }
            else {
                return $"{{ ${fieldName} }}";
            }
        }
        // Compatibility shim: legacy extend APIs default to read semantics.
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyArrayAccess(arrayType, MemberAccessOperation.Read, null, out result);
        public bool TryExtendTracingWithArrayAccess(ArrayType arrayType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyArrayAccess(arrayType, MemberAccessOperation.Read, sccIndex, out result);
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyCollectionAccess(collectionType, elementType, MemberAccessOperation.Read, null, out result);
        public bool TryExtendTracingWithCollectionAccess(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyCollectionAccess(collectionType, elementType, MemberAccessOperation.Read, sccIndex, out result);
        public bool TryExtendTracingWithMemberAccess(MemberReference member, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, null, out result);
        public bool TryExtendTracingWithMemberAccess(MemberReference member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyMemberAccess(member, MemberAccessOperation.Read, sccIndex, out result);

        public bool TryApplyArrayAccess(ArrayType arrayType, MemberAccessOperation operation, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
            var array = new ArrayElementLayer(arrayType);
            return TryApplyMemberAccess(array, operation, null, out result);
        }
        public bool TryApplyArrayAccess(ArrayType arrayType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
            var array = new ArrayElementLayer(arrayType);
            return TryApplyMemberAccess(array, operation, sccIndex, out result);
        }
        public bool TryApplyCollectionAccess(TypeReference collectionType, TypeReference elementType, MemberAccessOperation operation, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryApplyMemberAccess(collection, operation, null, out result);
        }
        public bool TryApplyCollectionAccess(TypeReference collectionType, TypeReference elementType, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
            var collection = new CollectionElementLayer(collectionType, elementType);
            return TryApplyMemberAccess(collection, operation, sccIndex, out result);
        }
        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyMemberAccess((MemberAccessStep)member, operation, null, out result);
        public bool TryApplyMemberAccess(MemberReference member, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result)
            => TryApplyMemberAccess((MemberAccessStep)member, operation, sccIndex, out result);
        private bool TryApplyMemberAccess(MemberAccessStep member, MemberAccessOperation operation, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
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
                else if (member is ArrayElementLayer array) {
                    isValidReference = !array.MemberType.IsTruelyValueType();
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
                    result = new StaticFieldTracingChain(
                        TracingStaticField,
                        EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                        ComponentAccessPath
                    );
                    return true;
                }

                return false;
            }

            if (member.IsSameLayer(EncapsulationHierarchy[0])) {
                result = new StaticFieldTracingChain(
                    TracingStaticField,
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
            var state = ComponentPathState.Exact(TracingStaticField.FieldType);

            foreach (var step in ComponentAccessPath) {
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

        private bool TryExtendComponentAccessPath(MemberAccessStep member, TypeFlowSccIndex? sccIndex, [NotNullWhen(true)] out StaticFieldTracingChain? result) {
            if (sccIndex is null) {
                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member]);
                return true;
            }

            if (member is SccLoopLayer loop) {
                if (!sccIndex.IsRecursiveScc(loop.SccId)) {
                    result = null;
                    return false;
                }

                var loopState = ComputeComponentPathState(sccIndex);

                if (loopState.Kind == ComponentStateKind.InScc) {
                    // Already inside a summarized SCC. Duplicate summaries (or mismatched ones) don't add information.
                    result = this;
                    return true;
                }

                var loopBaseType = loopState.ExactType ?? TracingStaticField.FieldType;
                if (!sccIndex.IsInSccIncludingBaseTypes(loopBaseType, loop.SccId)) {
                    // A loop summary that doesn't apply to the current typeRef would over-constrain the path; ignore it.
                    result = this;
                    return true;
                }

                if (!ComponentAccessPath.IsEmpty
                    && ComponentAccessPath[^1] is SccLoopLayer prev
                    && prev.SccId == loop.SccId) {

                    result = this;
                    return true;
                }

                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, loop]);
                return true;
            }

            var currentState = ComputeComponentPathState(sccIndex);

            // If we are currently "inside" a recursive SCC (unknown node), only allow exit edges.
            if (currentState.Kind == ComponentStateKind.InScc) {
                int activeSccId = currentState.SccId;
                if (!sccIndex.IsInSccIncludingBaseTypes(member.DeclaringType, activeSccId)) {
                    result = null;
                    return false;
                }
                if (member is ArrayElementLayer or CollectionElementLayer) {
                    result = this;
                    return true;
                }
                if (sccIndex.IsInSccIncludingBaseTypes(member.MemberType, activeSccId)) {
                    result = this;
                    return true;
                }
                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member]);
                return true;
            }

            var baseType = currentState.ExactType ?? TracingStaticField.FieldType;
            if (!sccIndex.TryGetRecursiveSccIdIncludingBaseTypes(baseType, out int sccId) || !sccIndex.IsRecursiveScc(sccId)) {
                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member]);
                return true;
            }

            if (!sccIndex.IsInSccIncludingBaseTypes(member.MemberType, sccId)) {
                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member]);
                return true;
            }

            if (WouldRevisitTypeWithinScc(TracingStaticField.FieldType, ComponentAccessPath, member, sccIndex, sccId)) {
                result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member, new SccLoopLayer(sccId, baseType)]);
                return true;
            }

            result = new StaticFieldTracingChain(TracingStaticField, [], [.. ComponentAccessPath, member]);
            return true;
        }

        private static bool WouldRevisitTypeWithinScc(
            TypeReference startType,
            ImmutableArray<MemberAccessStep> existingPath,
            MemberAccessStep nextStep,
            TypeFlowSccIndex sccIndex,
            int sccId) {

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = startType;

            bool inScc = sccIndex.IsInSccIncludingBaseTypes(current, sccId);
            if (inScc) {
                seen.Add(current.FullName);
            }

            foreach (var step in existingPath) {
                if (step is SccLoopLayer loop && loop.SccId == sccId) {
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

            var nextType = nextStep.MemberType;
            return sccIndex.IsInSccIncludingBaseTypes(nextType, sccId) && seen.Contains(nextType.FullName);
        }
        public bool TryTraceEnumeratorCurrent([NotNullWhen(true)] out StaticFieldTracingChain? result) {
            if (EncapsulationHierarchy.Length < 1) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy[0] is not EnumeratorLayer) {
                result = null;
                return false;
            }
            if (EncapsulationHierarchy.Length > 1 && EncapsulationHierarchy[1] is ArrayElementLayer or CollectionElementLayer) {
                result = new StaticFieldTracingChain(
                    TracingStaticField,
                    EncapsulationHierarchy.RemoveAt(0).RemoveAt(0),
                    ComponentAccessPath
                );
                return true;
            }
            if (EncapsulationHierarchy.Length is 1) {
                var typeRef = TracingStaticField.FieldType;
                if (!ComponentAccessPath.IsEmpty) {
                    typeRef = ComponentAccessPath.Last().MemberType;
                }
                if (typeRef is ArrayType at) {
                    result = new StaticFieldTracingChain(
                        TracingStaticField,
                        EncapsulationHierarchy.RemoveAt(0),
                        ComponentAccessPath.Add(new ArrayElementLayer(at))
                    );
                    return true;
                }
                var interfaces = typeRef.GetAllInterfaces().ToArray();
                var (idef, iref) = interfaces.FirstOrDefault(i => i.idef.FullName == EnumeratorLayer.GetEnumerableType(typeRef.Module).FullName);
                if (iref is GenericInstanceType git) {
                    result = new StaticFieldTracingChain(
                        TracingStaticField,
                        EncapsulationHierarchy.RemoveAt(0),
                        ComponentAccessPath.Add(new CollectionElementLayer(typeRef, git.GenericArguments.Last()))
                    );
                    return true;
                }
            }
            throw new NotSupportedException("Enumerator layer must be followed by ArrayElementLayer or CollectionElementLayer.");
        }

        public StaticFieldTracingChain CreateEncapsulatedInstance(MemberReference storedIn)
            => CreateEncapsulatedInstance((MemberAccessStep)storedIn, null);
        public StaticFieldTracingChain CreateEncapsulatedInstance(MemberReference storedIn, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance((MemberAccessStep)storedIn, sccIndex);
        public StaticFieldTracingChain CreateEncapsulatedArrayInstance(ArrayType arrayType)
            => CreateEncapsulatedInstance(new ArrayElementLayer(arrayType), null);
        public StaticFieldTracingChain CreateEncapsulatedArrayInstance(ArrayType arrayType, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance(new ArrayElementLayer(arrayType), sccIndex);
        public StaticFieldTracingChain CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType)
            => CreateEncapsulatedInstance(new CollectionElementLayer(collectionType, elementType), null);
        public StaticFieldTracingChain CreateEncapsulatedCollectionInstance(TypeReference collectionType, TypeReference elementType, TypeFlowSccIndex? sccIndex)
            => CreateEncapsulatedInstance(new CollectionElementLayer(collectionType, elementType), sccIndex);

        private StaticFieldTracingChain CreateEncapsulatedInstance(MemberAccessStep storedIn, TypeFlowSccIndex? sccIndex) {
            if (sccIndex is null) {
                return new StaticFieldTracingChain(this, storedIn);
            }

            var newHierarchy = EncapsulationHierarchy.Insert(0, storedIn);
            var normalizedHierarchy = NormalizeEncapsulationHierarchyWithSccLoops(newHierarchy, sccIndex);

            return new StaticFieldTracingChain(TracingStaticField, normalizedHierarchy, ComponentAccessPath);
        }

        private static ImmutableArray<MemberAccessStep> NormalizeEncapsulationHierarchyWithSccLoops(
            ImmutableArray<MemberAccessStep> hierarchy,
            TypeFlowSccIndex sccIndex) {

            if (hierarchy.IsEmpty) {
                return hierarchy;
            }

            var builder = ImmutableArray.CreateBuilder<MemberAccessStep>(hierarchy.Length);

            int? activeSccId = null;
            bool hasLoopSummary = false;
            HashSet<string>? seen = null;

            foreach (var step in hierarchy) {
                if (step is SccLoopLayer loop) {
                    activeSccId = loop.SccId;
                    hasLoopSummary = true;
                    seen = null;

                    if (builder.Count == 0 || builder[^1] is not SccLoopLayer prev || prev.SccId != loop.SccId) {
                        builder.Add(loop);
                    }

                    continue;
                }

                if (!sccIndex.TryGetRecursiveSccIdIncludingBaseTypes(step.DeclaringType, out int sccId)
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
        public StaticFieldTracingChain? CreateEncapsulatedEnumeratorInstance() {
            if (EncapsulationHierarchy.IsEmpty) {
                var typeRef = TracingStaticField.FieldType;
                if (!ComponentAccessPath.IsEmpty) {
                    typeRef = ComponentAccessPath.Last().MemberType;
                }
                var typeDef = typeRef.TryResolve();
                if (typeRef is not ArrayType && !typeRef.GetAllInterfaces()
                    .Select(x => x.idef.FullName)
                    .Contains(EnumeratorLayer.GetEnumerableType(typeRef.Module).FullName)) {
                    return null;
                }

                return new StaticFieldTracingChain(this, new EnumeratorLayer(typeRef));
            }
            if (EncapsulationHierarchy[0] is ArrayElementLayer or CollectionElementLayer) {
                var collectionLayer = EncapsulationHierarchy[0];
                return new StaticFieldTracingChain(this, new EnumeratorLayer(collectionLayer.DeclaringType));
            }
            // if there has already enumerator, we don't need to create a nested one
            if (EncapsulationHierarchy[0] is EnumeratorLayer) {
                return this;
            }
            return null;
        }
        public static StaticFieldTracingChain? CombineStaticFieldTraces(StaticFieldTracingChain outerPart, StaticFieldTracingChain innerPart)
            => CombineStaticFieldTraces(outerPart, innerPart, null);

        public static StaticFieldTracingChain? CombineStaticFieldTraces(StaticFieldTracingChain outerPart, StaticFieldTracingChain innerPart, TypeFlowSccIndex? sccIndex) {
            if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                if (!TryMatchPrefixWithSccLoops(innerPart.ComponentAccessPath, outerPart.EncapsulationHierarchy, sccIndex, out int consumedInner, out int consumedOuter)) {
                    return null;
                }

                var combinedEncapsulation = innerPart.EncapsulationHierarchy.AddRange(outerPart.EncapsulationHierarchy.Skip(consumedOuter));
                if (sccIndex is not null) {
                    combinedEncapsulation = NormalizeEncapsulationHierarchyWithSccLoops(combinedEncapsulation, sccIndex);
                }

                return new StaticFieldTracingChain(
                    outerPart.TracingStaticField,
                    combinedEncapsulation,
                    innerPart.ComponentAccessPath.Skip(consumedInner)
                );
            }
            else if (innerPart.ComponentAccessPath.Length == 0 && outerPart.EncapsulationHierarchy.Length != 0) {
                var combinedEncapsulation = innerPart.EncapsulationHierarchy.AddRange(outerPart.EncapsulationHierarchy);
                if (sccIndex is not null) {
                    combinedEncapsulation = NormalizeEncapsulationHierarchyWithSccLoops(combinedEncapsulation, sccIndex);
                }

                return new StaticFieldTracingChain(outerPart.TracingStaticField,
                    combinedEncapsulation,
                    outerPart.ComponentAccessPath
                );
            }
            else if (innerPart.ComponentAccessPath.Length != 0 && outerPart.EncapsulationHierarchy.Length == 0) {
                if (sccIndex is null) {
                    return new StaticFieldTracingChain(outerPart.TracingStaticField,
                        [],
                        ConcatWithoutOverlappingPrefix(outerPart.ComponentAccessPath, innerPart.ComponentAccessPath)
                    );
                }

                var combined = new StaticFieldTracingChain(outerPart.TracingStaticField, [], outerPart.ComponentAccessPath);
                foreach (var step in innerPart.ComponentAccessPath) {
                    if (!combined.TryExtendComponentAccessPath(step, sccIndex, out var next)) {
                        return null;
                    }
                    combined = next;
                }

                return combined;
            }
            else {
                return new StaticFieldTracingChain(outerPart.TracingStaticField,
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

                var step = innerComponent[i];

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

            int maxOverlap = Math.Min(left.Length, right.Length);

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

        public override bool Equals(object? obj) => Equals(obj as StaticFieldTracingChain);
        public bool Equals(StaticFieldTracingChain? other) =>
            other != null &&
            other.Key == Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}
