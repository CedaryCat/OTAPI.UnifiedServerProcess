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
                var field = ((FieldReference)inst.Operand).TryResolve();
                if (field is not null) {
                    modifiedFields.Add(field);
                }
            }
            else if (inst.OpCode == OpCodes.Ldsflda) {
                foreach (var usage in AnalyzeStackTopValueUsage(method, inst)) {
                    modificationOperations.Add(usage);
                }
                var field = ((FieldReference)inst.Operand).TryResolve();
                if (field is not null) {
                    modifiedFields.Add(field);
                }
            }
            else if (point.IsReferencedStaticFieldModified(method, inst, out var modified, out modificationOperations)) {
                foreach (var modifiedFieldTraces in modified.Values) {
                    modifiedFields.Add(modifiedFieldTraces.TrackingStaticField);
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
            [NotNullWhen(true)] out Dictionary<string, SingleStaticFieldTrace>? modified,
            [NotNullWhen(true)] out HashSet<Instruction>? modificationOperations) {

            modified = null;
            modificationOperations = null;

            if (!point.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(method.GetIdentifier(), out var data)
                || !data.StackValueTraces.TryGetTrace(StaticFieldReferenceData.GenerateStackKey(method, pushedFieldReference), out var trace)) {
                return false;
            }

            modified = [];
            modificationOperations = [];

            foreach (var usage in AnalyzeStackTopValueUsage(method, pushedFieldReference)) {
                if (usage.OpCode != OpCodes.Call && usage.OpCode != OpCodes.Callvirt) {
                    continue;
                }
                var calleeRef = (MethodReference)usage.Operand;
                if (!point.ParamModificationAnalyzer.ModifiedParameters.TryGetValue(calleeRef.GetIdentifier(), out var paramData)) {
                    continue;
                }
                foreach (var path in AnalyzeParametersSources(method, usage, point.GetMethodJumpSites(method))) {
                    for (int paramIndex = 0; paramIndex < path.ParametersSources.Length; paramIndex++) {
                        if (!paramData.TryGetValue(paramIndex, out var willBeModified) ||
                            !path.ParametersSources[paramIndex].Instructions.Contains(pushedFieldReference)) {
                            continue;
                        }
                        
                        modificationOperations.Add(usage);

                        foreach (var referencedFieldTracing in trace.TrackedStaticFields.Values) {
                            CollectModificationChains(willBeModified, referencedFieldTracing, out var chains, out var modifiedField);
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

        private static void MergeModificationChains(Dictionary<string, SingleStaticFieldTrace> destination, List<MemberAccessStep[]> source, FieldDefinition modifiedField) {
            if (source.Count > 0) {
                var collected = source.Select(x => new StaticFieldTrackingChain(modifiedField, [], x));

                if (!destination.TryGetValue(modifiedField.GetIdentifier(), out var staticFieldTrace)) {
                    destination.Add(modifiedField.GetIdentifier(), staticFieldTrace = new(modifiedField, collected));
                }
                else {
                    foreach (var part in collected) {
                        staticFieldTrace.PartTrackingPaths.Add(part);
                    }
                }
            }
        }

        private static void CollectModificationChains(ParamModifications willBeModified, SingleStaticFieldTrace referencedFieldTracing, out List<MemberAccessStep[]> chains, out FieldDefinition modifiedField) {
            chains = [];
            modifiedField = referencedFieldTracing.TrackingStaticField;
            foreach (var part in referencedFieldTracing.PartTrackingPaths) {
                foreach (var modification in willBeModified.modifications) {
                    if (part.EncapsulationHierarchy.Length > 0) {
                        if (modification.ModificationAccessPath.Length <= part.EncapsulationHierarchy.Length) {
                            continue;
                        }
                        bool isLeadingChain = true;
                        for (int i = 0; i < part.ComponentAccessPath.Length; i++) {
                            if (modification.ModificationAccessPath[i].IsSameLayer(part.EncapsulationHierarchy[i])) {
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
