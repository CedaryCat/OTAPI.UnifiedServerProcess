using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class StaticConstructorProcessor(MethodCallGraph callGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature, IJumpSitesCacheFeature
    {
        public MethodCallGraph MethodCallGraph => callGraph;
        public string Name => nameof(StaticConstructorProcessor);

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            var module = source.MainModule;
            foreach (var type in module.GetAllTypes().ToArray()) {
                if (type.Name.StartsWith('<')) {
                    continue;
                }
                var cctor = type.GetStaticConstructor();
                if (cctor is null) {
                    continue;
                }
                if (!this.CheckUsedContextBoundField(source.RootContextDef, source.OriginalToInstanceConvdField, cctor)) {
                    continue;
                }
                if (!source.OriginalToContextType.TryGetValue(type.FullName, out var contextType)) {
                    contextType = new ContextTypeData(type, source.RootContextDef, callGraph.MediatedCallGraph, ref source.OriginalToContextType);
                }

                HashSet<string> checkedFields = [];

                foreach (var inst in cctor.Body.Instructions) {
                    List<Instruction> checkSource = [];
                    if (inst.OpCode == OpCodes.Stsfld) {
                        checkSource.Add(inst);
                    }
                    else if (inst.OpCode == OpCodes.Ldflda) {
                        checkSource.AddRange(MonoModCommon.Stack.AnalyzeStackTopValueUsage(cctor, inst));
                    }
                    else {
                        continue;
                    }

                    var field = ((FieldReference)inst.Operand).TryResolve();
                    if (field is null) {
                        continue;
                    }
                    if (source.OriginalToInstanceConvdField.ContainsKey(field.FullName)) {
                        continue;
                    }
                    if (checkedFields.Contains(field.FullName)) {
                        continue;
                    }

                    if (!CheckUsedContext(source, cctor, checkSource)) {
                        continue;
                    }

                    checkedFields.Add(field.FullName);

                    var newfield = new FieldDefinition(field.Name, field.Attributes & ~FieldAttributes.Static, field.FieldType);
                    newfield.CustomAttributes.AddRange(field.CustomAttributes.Select(c => c.Clone()));
                    contextType.ContextTypeDef.Fields.Add(newfield);
                    source.OriginalToInstanceConvdField.Add(field.FullName, newfield);
                }
            }
        }

        bool CheckUsedContext(PatcherArgumentSource arg, MethodDefinition cctor, List<Instruction> checkSource) {
            var sourceInsts = ExtractSources(cctor, checkSource);
            foreach (var check in sourceInsts) {
                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn) {
                    var methodDef = ((MethodReference)check.Operand).TryResolve();
                    if (methodDef is null) {
                        continue;
                    }
                    if (this.CheckUsedContextBoundField(arg.RootContextDef, arg.OriginalToInstanceConvdField, methodDef)) {
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
