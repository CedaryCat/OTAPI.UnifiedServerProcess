using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
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
            var module = source.MainModule;
            foreach (var type in module.GetAllTypes().ToArray()) {
                if (type.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                var cctor = type.GetStaticConstructor();
                if (cctor is null) {
                    continue;
                }
                if (!this.CheckUsedContextBoundField(source.OriginalToInstanceConvdField, cctor)) {
                    continue;
                }
                if (!source.OriginalToContextType.TryGetValue(type.FullName, out var contextType)) {
                    contextType = new ContextTypeData(type, source.RootContextDef, MethodCallGraph.MediatedCallGraph, ref source.OriginalToContextType);
                }

                HashSet<string> checkedFields = [];
                AggregatedStaticFieldProvenance? trace = null;

                foreach (var inst in cctor.Body.Instructions) {

                    if (!this.IsAboutStaticFieldModification(cctor, inst, out var fields, out var checkSource)) {
                        continue;
                    }

                    if (!CheckUsedContext(source, cctor, checkSource)) {
                        continue;
                    }

                    foreach (var field in fields) {
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

                foreach (var inst in cctor.Body.Instructions) {

                    if (!analyzers.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(cctor.GetIdentifier(), out var data)
                        || !data.StackValueTraces.TryGetTrace(StaticFieldUsageTrack.GenerateStackKey(cctor, inst), out trace)) {
                        continue;
                    }

                    foreach (var fieldReferenceUsage in MonoModCommon.Stack.AnalyzeStackTopValueUsage(cctor, inst)) {
                        if (fieldReferenceUsage.OpCode != OpCodes.Call && fieldReferenceUsage.OpCode != OpCodes.Callvirt) {
                            continue;
                        }
                        var calleeRef = (MethodReference)fieldReferenceUsage.Operand;

                        if (!analyzers.StaticFieldReferenceAnalyzer.AnalyzedMethods.TryGetValue(calleeRef.GetIdentifier(), out var referencedStaticFieldData)
                            || !analyzers.ParameterFlowAnalyzer.AnalyzedMethods.TryGetValue(calleeRef.GetIdentifier(), out var parameterFlowData)) {
                            continue;
                        }

                        var calleeDef = calleeRef.TryResolve();
                        if (calleeDef is null || !calleeDef.HasBody) {
                            continue;
                        }

                        var paths = MonoModCommon.Stack.AnalyzeParametersSources(cctor, fieldReferenceUsage, this.GetMethodJumpSites(cctor));

                        foreach (var path in paths) {
                            for (int paramIndex = 0; paramIndex < path.ParametersSources.Length; paramIndex++) {
                                var loadParam = path.ParametersSources[paramIndex];
                                var usedAsParam = loadParam.Parameter;
                                if (!loadParam.Instructions.Contains(inst)) {
                                    continue;
                                }
                                foreach (var calleeInst in calleeDef.Body.Instructions) {
                                    if (this.IsAboutStaticFieldModification(calleeDef, calleeInst, out var otherFields, out var calleeModificationOperations)) {
                                        foreach (var fieldTraceData in trace.TracedStaticFields.Values) {
                                            if (!source.OriginalToInstanceConvdField.ContainsKey(fieldTraceData.TracingStaticField.GetIdentifier())) {
                                                continue;
                                            }

                                            foreach (var field in otherFields) {
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
            var sourceInsts = ExtractSources(cctor, checkSource);
            foreach (var check in sourceInsts) {
                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn) {
                    var methodDef = ((MethodReference)check.Operand).TryResolve();
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


            var jumpSite = this.GetMethodJumpSites(cctor);

            Stack<Instruction> stack = [];
            foreach (var checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                var check = stack.Pop();
                if (!extracted.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(cctor, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only pop value from stack
                else if (MonoModCommon.Stack.GetPopCount(cctor.Body, check) > 0) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(cctor, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only push value to stack
                else if (MonoModCommon.IL.TryGetReferencedVariable(cctor, check, out var local)) {
                    if (checkedLocals.Contains(local)) {
                        continue;
                    }
                    foreach (var inst in cctor.Body.Instructions) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(cctor, inst, out var otherLocal) || otherLocal.Index != local.Index) {
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
