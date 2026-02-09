using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParamModificationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class StaticConstructorProcessor(AnalyzerGroups analyzers) : IGeneralArgProcessor, IMethodCheckCacheFeature, IStaticModificationCheckFeature, IJumpSitesCacheFeature
    {
        public MethodCallGraph MethodCallGraph => analyzers.MethodCallGraph;
        public StaticFieldReferenceAnalyzer StaticFieldReferenceAnalyzer => analyzers.StaticFieldReferenceAnalyzer;
        public ParamModificationAnalyzer ParamModificationAnalyzer => analyzers.ParamModificationAnalyzer;
        public string Name => nameof(StaticConstructorProcessor);


        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            ModuleDefinition module = source.MainModule;
            foreach (TypeDefinition? type in module.GetAllTypes().ToArray()) {
                if (type.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                MethodDefinition? cctor = type.GetStaticConstructor();
                if (cctor is null) {
                    continue;
                }
                if (!this.CheckUsedContextBoundField(source.OriginalToInstanceConvdField, cctor)) {
                    continue;
                }
                if (!source.OriginalToContextType.TryGetValue(type.FullName, out ContextTypeData? contextType)) {
                    contextType = new ContextTypeData(type, source.RootContextDef, MethodCallGraph.MediatedCallGraph, ref source.OriginalToContextType);
                }

                HashSet<string> checkedFields = [];
                AggregatedStaticFieldProvenance? trace = null;

                foreach (Instruction? inst in cctor.Body.Instructions) {

                    if (!this.IsAboutStaticFieldModification(cctor, inst, out HashSet<FieldDefinition>? fields, out HashSet<Instruction>? checkSource)) {
                        continue;
                    }

                    if (!CheckUsedContext(source, cctor, checkSource)) {
                        continue;
                    }

                    foreach (FieldDefinition field in fields) {
                        if (source.OriginalToInstanceConvdField.ContainsKey(field.GetIdentifier())) {
                            continue;
                        }
                        if (!checkedFields.Add(field.GetIdentifier())) {
                            continue;
                        }

                        var newfield = new FieldDefinition(field.Name, field.Attributes & ~FieldAttributes.Static, field.FieldType);
                        newfield.CustomAttributes.AddRange(field.CustomAttributes.Select(c => c.Clone()));
                        contextType.ContextTypeDef.Fields.Add(newfield);
                        source.OriginalToInstanceConvdField.Add(field.GetIdentifier(), newfield);
                    }
                }

                foreach (Instruction? inst in cctor.Body.Instructions) {

                    if (!analyzers.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(cctor.GetIdentifier(), out StaticFieldUsageTrack? data)
                        || !data.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(cctor, inst), out trace)) {
                        continue;
                    }

                    foreach (Instruction fieldReferenceUsage in MonoModCommon.Stack.TraceStackValueConsumers(cctor, inst)) {
                        if (fieldReferenceUsage.OpCode != OpCodes.Call && fieldReferenceUsage.OpCode != OpCodes.Callvirt) {
                            continue;
                        }
                        var calleeRef = (MethodReference)fieldReferenceUsage.Operand;

                        if (!analyzers.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(calleeRef.GetIdentifier(), out StaticFieldUsageTrack? referencedStaticFieldData)
                            || !analyzers.ParameterFlowAnalyzer.AnalyzedMethods.TryGetValue(calleeRef.GetIdentifier(), out ParameterUsageTrack? parameterFlowData)) {
                            continue;
                        }

                        MethodDefinition? calleeDef = calleeRef.TryResolve();
                        if (calleeDef is null || !calleeDef.HasBody) {
                            continue;
                        }

                        MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paths = MonoModCommon.Stack.AnalyzeParametersSources(cctor, fieldReferenceUsage, this.GetMethodJumpSites(cctor));

                        foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path in paths) {
                            for (int paramIndex = 0; paramIndex < path.ParametersSources.Length; paramIndex++) {
                                MonoModCommon.Stack.ParameterSource loadParam = path.ParametersSources[paramIndex];
                                ParameterDefinition usedAsParam = loadParam.Parameter;
                                if (!loadParam.Instructions.Contains(inst)) {
                                    continue;
                                }
                                foreach (Instruction? calleeInst in calleeDef.Body.Instructions) {
                                    if (this.IsAboutStaticFieldModification(calleeDef, calleeInst, out HashSet<FieldDefinition>? otherFields, out HashSet<Instruction>? calleeModificationOperations)) {
                                        foreach (StaticFieldProvenance fieldTraceData in trace.TracedStaticFields.Values) {
                                            if (!source.OriginalToInstanceConvdField.ContainsKey(fieldTraceData.TracingStaticField.GetIdentifier())) {
                                                continue;
                                            }

                                            foreach (FieldDefinition field in otherFields) {
                                                if (source.OriginalToInstanceConvdField.ContainsKey(field.GetIdentifier())) {
                                                    continue;
                                                }
                                                if (!checkedFields.Add(field.GetIdentifier())) {
                                                    continue;
                                                }

                                                var newfield = new FieldDefinition(field.Name, field.Attributes & ~FieldAttributes.Static, field.FieldType);
                                                newfield.CustomAttributes.AddRange(field.CustomAttributes.Select(c => c.Clone()));
                                                contextType.ContextTypeDef.Fields.Add(newfield);
                                                source.OriginalToInstanceConvdField.Add(field.GetIdentifier(), newfield);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        bool CheckUsedContext(PatcherArgumentSource arg, MethodDefinition cctor, params IEnumerable<Instruction> checkSource) {
            Instruction[] sourceInsts = ExtractSources(cctor, checkSource);
            foreach (Instruction check in sourceInsts) {
                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn) {
                    MethodDefinition? methodDef = ((MethodReference)check.Operand).TryResolve();
                    if (methodDef is null) {
                        continue;
                    }
                    if (this.CheckUsedContextBoundField(arg.OriginalToInstanceConvdField, methodDef)) {
                        return true;
                    }
                }
            }
            return false;
        }
        Instruction[] ExtractSources(MethodDefinition cctor, params IEnumerable<Instruction> extractSources) {
            HashSet<Instruction> extracted = [];
            HashSet<VariableDefinition> checkedLocals = [];


            Dictionary<Instruction, List<Instruction>> jumpSite = this.GetMethodJumpSites(cctor);

            Stack<Instruction> stack = [];
            foreach (Instruction checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                Instruction check = stack.Pop();
                if (!extracted.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path in MonoModCommon.Stack.AnalyzeParametersSources(cctor, check, jumpSite)) {
                        foreach (MonoModCommon.Stack.ParameterSource source in path.ParametersSources) {
                            foreach (Instruction inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only pop value from stack
                else if (MonoModCommon.Stack.GetPopCount(cctor.Body, check) > 0) {
                    foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(cctor, check, jumpSite)) {
                        foreach (MonoModCommon.Stack.InstructionArgsSource source in path.ParametersSources) {
                            foreach (Instruction inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only push value to stack
                else if (MonoModCommon.IL.TryGetReferencedVariable(cctor, check, out VariableDefinition? local)) {
                    if (checkedLocals.Contains(local)) {
                        continue;
                    }
                    foreach (Instruction? inst in cctor.Body.Instructions) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(cctor, inst, out VariableDefinition? otherLocal) || otherLocal.Index != local.Index) {
                            continue;
                        }
                        // store local
                        if (MonoModCommon.Stack.GetPopCount(cctor.Body, inst) > 0) {
                            stack.Push(inst);
                        }
                        else {
                            extracted.Add(inst);
                        }
                    }
                    checkedLocals.Add(local);
                }
            }

            return extracted.ToArray();
        }
    }
}
