using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static OTAPI.UnifiedServerProcess.Commons.MonoModCommon.Stack;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IStaticModificationCheckFeature : IJumpSitesCacheFeature
    {
        public StaticFieldReferenceAnalyzer StaticFieldReferenceAnalyzer { get; }
        public ParamModificationAnalyzer ParamModificationAnalyzer { get; }
    }
    public static class StaticModificationCheckFeatureExtensions
    {
        public static bool IsAboutStaticFieldModification(this IStaticModificationCheckFeature point,
            MethodDefinition method,
            Instruction inst,
            [NotNullWhen(true)] out HashSet<FieldDefinition>? modifiedFields, [NotNullWhen(true)] out HashSet<Instruction>? modificationOperations) {

            modifiedFields = [];
            modificationOperations = [];

            if (inst.OpCode == OpCodes.Stsfld) {
                modificationOperations.Add(inst);
                FieldDefinition? field = ((FieldReference)inst.Operand).TryResolve();
                if (field is not null) {
                    modifiedFields.Add(field);
                }
            }
            else if (inst.OpCode == OpCodes.Ldsflda) {
                foreach (Instruction usage in TraceStackValueConsumers(method, inst)) {
                    modificationOperations.Add(usage);
                }
                FieldDefinition? field = ((FieldReference)inst.Operand).TryResolve();
                if (field is not null) {
                    modifiedFields.Add(field);
                }
            }
            else if (point.IsReferencedStaticFieldModified(method, inst, out Dictionary<string, StaticFieldProvenance>? modified, out modificationOperations)) {
                foreach (StaticFieldProvenance modifiedFieldTraces in modified.Values) {
                    modifiedFields.Add(modifiedFieldTraces.TracingStaticField);
                }
            }
            else {
                modifiedFields = null;
                modificationOperations = null;
                return false;
            }

            if (modifiedFields.Count == 0) {
                modifiedFields = null;
                modificationOperations = null;
                return false;
            }

            return true;
        }
        public static bool IsReferencedStaticFieldModified(this IStaticModificationCheckFeature point,
            MethodDefinition method,
            Instruction pushedFieldReference,
            [NotNullWhen(true)] out Dictionary<string, StaticFieldProvenance>? modified,
            [NotNullWhen(true)] out HashSet<Instruction>? modificationOperations) {

            modified = null;
            modificationOperations = null;

            if (!point.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(method.GetIdentifier(), out StaticFieldUsageTrack? data)
                || !data.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(method, pushedFieldReference), out AggregatedStaticFieldProvenance? trace)) {
                return false;
            }

            modified = [];
            modificationOperations = [];

            foreach (Instruction usage in TraceStackValueConsumers(method, pushedFieldReference)) {
                if (usage.OpCode != OpCodes.Call && usage.OpCode != OpCodes.Callvirt) {
                    continue;
                }
                var calleeRef = (MethodReference)usage.Operand;
                if (!point.ParamModificationAnalyzer.ModifiedParameters.TryGetValue(calleeRef.GetIdentifier(), out System.Collections.Immutable.ImmutableDictionary<int, ParameterMutationInfo>? paramData)) {
                    continue;
                }
                foreach (FlowPath<ParameterSource> path in AnalyzeParametersSources(method, usage, point.GetMethodJumpSites(method))) {
                    for (int paramIndex = 0; paramIndex < path.ParametersSources.Length; paramIndex++) {
                        if (!paramData.TryGetValue(paramIndex, out ParameterMutationInfo? willBeModified) ||
                            !path.ParametersSources[paramIndex].Instructions.Contains(pushedFieldReference)) {
                            continue;
                        }

                        modificationOperations.Add(usage);

                        foreach (StaticFieldProvenance referencedFieldTracing in trace.TracedStaticFields.Values) {
                            CollectModificationChains(willBeModified, referencedFieldTracing, out List<MemberAccessStep[]>? chains, out FieldDefinition? modifiedField);
                            MergeModificationChains(modified, chains, modifiedField);
                        }
                    }
                }
            }

            if (modified.Count == 0) {
                modified = null;
                modificationOperations = null;
                return false;
            }

            return true;
        }

        private static void MergeModificationChains(Dictionary<string, StaticFieldProvenance> destination, List<MemberAccessStep[]> source, FieldDefinition modifiedField) {
            if (source.Count > 0) {
                IEnumerable<StaticFieldTracingChain> collected = source.Select(x => new StaticFieldTracingChain(modifiedField, [], x));

                if (!destination.TryGetValue(modifiedField.GetIdentifier(), out StaticFieldProvenance? staticFieldTrace)) {
                    destination.Add(modifiedField.GetIdentifier(), staticFieldTrace = new(modifiedField, collected));
                }
                else {
                    foreach (StaticFieldTracingChain? part in collected) {
                        staticFieldTrace.PartTracingPaths.Add(part);
                    }
                }
            }
        }

        private static void CollectModificationChains(ParameterMutationInfo willBeModified, StaticFieldProvenance referencedFieldTracing, out List<MemberAccessStep[]> chains, out FieldDefinition modifiedField) {
            chains = [];
            modifiedField = referencedFieldTracing.TracingStaticField;
            foreach (StaticFieldTracingChain part in referencedFieldTracing.PartTracingPaths) {
                foreach (ModifiedComponent modification in willBeModified.Mutations) {
                    if (part.EncapsulationHierarchy.Length > 0) {
                        if (modification.ModificationAccessPath.Length <= part.EncapsulationHierarchy.Length) {
                            continue;
                        }
                        bool isLeadingChain = true;
                        for (int i = 0; i < part.ComponentAccessPath.Length; i++) {
                            if (!modification.ModificationAccessPath[i].IsSameLayer(part.EncapsulationHierarchy[i])) {
                                isLeadingChain = false;
                                break;
                            }
                        }
                        if (isLeadingChain) {
                            chains.Add([.. part.ComponentAccessPath, .. modification.ModificationAccessPath.Skip(part.EncapsulationHierarchy.Length)]);
                        }
                    }
                    else {
                        chains.Add([.. part.ComponentAccessPath, .. modification.ModificationAccessPath]);
                    }
                }
            }
        }
    }
}
