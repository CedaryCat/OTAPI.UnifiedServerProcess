using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    public class GlobalExtractPatcher(ILogger logger, MethodCallGraph callGraph, MethodDefinition[] initialMethods) : GeneralPatcher(logger), IJumpSitesCacheFeature, IMethodBehaivorFeature, IMethodCheckCacheFeature
    {
        public DelegateInvocationGraph DelegateInvocationGraph => callGraph.DelegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => callGraph.MethodInheritanceGraph;
        public MethodCallGraph MethodCallGraph => callGraph;
        public override string Name => nameof(GlobalExtractPatcher);

        public override void Patch(PatcherArguments arguments) {

            var globalInitializer = arguments.MainModule.GetType(Constants.GlobalInitializerTypeName);

            var workQueue = new Stack<MethodDefinition>(initialMethods);
            var visited = new Dictionary<string, MethodDefinition>();
            Dictionary<string, Dictionary<string, List<MethodDefinition>>> methodsCallPaths = [];

            while (workQueue.TryPop(out var method)) {
                var mid = method.GetIdentifier();
                if (!visited.TryAdd(mid, method)) {
                    continue;
                }

                if (!methodsCallPaths.TryGetValue(mid, out var callPaths)) {
                    callPaths = new() {
                        { method.GetDebugName(), [method] },
                    };
                }

                var firstCallPath = callPaths.First();

                ProcessMethod(
                    method,
                    arguments,
                    globalInitializer,
                    firstCallPath.Value,
                    out var addedCallees
                );

                foreach (var callee in addedCallees) {
                    var calleeID = callee.GetIdentifier();
                    if (visited.ContainsKey(calleeID)) {
                        continue;
                    }

                    var calleeCallPath = firstCallPath.Key + " → " + callee.GetDebugName();
                    if (!methodsCallPaths.TryGetValue(calleeID, out var calleeCallPaths)) {
                        methodsCallPaths.Add(calleeID, calleeCallPaths = []);
                    }

                    calleeCallPaths.Add(calleeCallPath, [.. firstCallPath.Value, callee]);

                    workQueue.Push(callee);
                }
            }
        }
        readonly Dictionary<string, int> beenCalledCount = [];
        private void ProcessMethod(
            MethodDefinition method,
            PatcherArguments source,
            TypeDefinition globalInitializer,
            List<MethodDefinition> callPath,
            out MethodDefinition[] callees) {

            callees = [];

            Dictionary<string, MethodDefinition> myCalingMethods = [];

            if (!method.HasBody) {
                return;
            }

            if (callGraph.MediatedCallGraph.TryGetValue(method.GetIdentifier(), out var calls)) {
                foreach (var useds in calls.UsedMethods) {
                    foreach (var call in useds.ImplementedMethods()) {
                        myCalingMethods.TryAdd(call.GetIdentifier(), call);
                    }
                }
            }

            Dictionary<string, HashSet<Instruction>> transformInsts = [];
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap = [];

            foreach (var inst in method.Body.Instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                    case Code.Ldsflda:
                        HandleLoadStaticField(this, source, method, transformInsts, localMap, inst, inst.OpCode == OpCodes.Ldsflda);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(this, source, method, transformInsts, localMap, inst);
                        break;
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                    case Code.Stloc_S:
                    case Code.Stloc:
                        HandleStoreLocal(this, source, method, transformInsts, localMap, inst);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        var callee = ((MethodReference)inst.Operand).TryResolve();
                        if (callee is null) {
                            break;
                        }
                        var calleeId = callee.GetIdentifier();
                        beenCalledCount.TryAdd(calleeId, 0);
                        beenCalledCount[calleeId]++;
                        break;
                }
            }

            foreach (var inst in method.Body.Instructions) {
                if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var local)) {
                    continue;
                }
                if (!localMap.TryGetValue(local, out var tuple)) {
                    continue;
                }
                foreach (var fieldKV in tuple.fields) {
                    if (!transformInsts.TryGetValue(fieldKV.Key, out var extracteds)) {
                        transformInsts.Add(fieldKV.Key, extracteds = []);
                    }
                    switch (inst.OpCode.Code) {
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                        case Code.Stloc:
                            extracteds.Add(inst);
                            ExtractSources(this, method, fieldKV.Value, extracteds, localMap, inst);
                            break;
                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                        case Code.Ldloc_S:
                        case Code.Ldloca_S:
                        case Code.Ldloca:
                            extracteds.Add(inst);
                            TrackUsage(this, method, fieldKV.Value, extracteds, localMap, inst);
                            break;
                    }
                }
            }


            HashSet<Instruction> extractedStaticInsts = [];

            foreach (var kv in transformInsts) {

                foreach (var inst in kv.Value) {
                    switch (inst.OpCode.Code) {
                        case Code.Ldsfld:
                        case Code.Ldsflda:
                        case Code.Stsfld: {
                                var fieldRef = (FieldReference)inst.Operand;
                                var id = fieldRef.GetIdentifier();
                                if (source.ModifiedStaticFields.ContainsKey(id)) {
                                    source.InitialStaticFields.Remove(kv.Key);
                                    source.UnmodifiedStaticFields.Remove(kv.Key);
                                    source.ModifiedStaticFields.TryAdd(kv.Key, initFieldDef);
                                    goto cancel;
                                }
                            }
                            break;
                        case Code.Call:
                        case Code.Callvirt:
                        case Code.Newobj: {
                                var methodRef = (MethodReference)inst.Operand;
                                var methodDef = methodRef.TryResolve();
                                if (methodDef is null) {
                                    break;
                                }
                                if (this.CheckUsedContextBoundField(source.ModifiedStaticFields, methodDef)) {
                                    source.InitialStaticFields.Remove(kv.Key);
                                    source.UnmodifiedStaticFields.Remove(kv.Key);
                                    source.ModifiedStaticFields.TryAdd(kv.Key, initFieldDef);
                                    goto cancel;
                                }
                            }
                            break;
                    }
                    if (MonoModCommon.IL.TryGetReferencedParameter(method, inst, out var parameter)) {
                        source.InitialStaticFields.Remove(kv.Key);
                        source.UnmodifiedStaticFields.Remove(kv.Key);
                        source.ModifiedStaticFields.TryAdd(kv.Key, initFieldDef);
                        goto cancel;
                    }
                }
                foreach (var staticInst in kv.Value) {
                    extractedStaticInsts.Add(staticInst);
                }
                continue;
            cancel:;
            }

            callees = myCalingMethods.Values.ToArray();

            bool canBeExpanded = source.InitialMethods.Contains(method) || (beenCalledCount.TryGetValue(method.GetIdentifier(), out var count) && count == 1);

            if (canBeExpanded && extractedStaticInsts.Count > 0) {

                MethodDefinition[] origMethodCallChain = [globalInitializer.Method(Constants.GlobalInitializerEntryPointName), .. callPath];
                MethodDefinition[] methodCallChain = new MethodDefinition[origMethodCallChain.Length];
                for (int i = 0; i < origMethodCallChain.Length; i++) {
                    MethodDefinition chainedMethod = origMethodCallChain[i];

                    string generatedCallerName;
                    if (i == 0) {
                        generatedCallerName = chainedMethod.Name;
                        methodCallChain[i] = chainedMethod;
                    }
                    else {
                        var origCaller = origMethodCallChain[i];
                        generatedCallerName = chainedMethod.DeclaringType.Name + "_" + origCaller.Name;
                    }

                    var generatedMethod = globalInitializer.Methods.Where(m => m.Name == generatedCallerName).FirstOrDefault();
                    if (generatedMethod is null) {
                        generatedMethod = new MethodDefinition(generatedCallerName, Constants.Modifiers.GlobalInitialize, globalInitializer.Module.TypeSystem.Void);
                        var body = generatedMethod.Body = new MethodBody(generatedMethod);
                        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                        globalInitializer.Methods.Add(generatedMethod);

                        var caller = methodCallChain[i - 1];
                        var ret = caller.Body.Instructions.Last();

                        var call = Instruction.Create(OpCodes.Call, generatedMethod);
                        caller.Body.GetILProcessor().InsertBefore(ret, call);

                        foreach (var localData in localMap.Values) {
                            generatedMethod.Body.Variables.Add(localData.local);
                        }
                    }

                    methodCallChain[i] = generatedMethod;
                }

                var generated = methodCallChain[^1];
                var returnInst = generated.Body.Instructions.Last();
                var ilProcessor = generated.Body.GetILProcessor();

                foreach (var inst in method.Body.Instructions) {
                    if (extractedStaticInsts.Contains(inst)) {
                        var clone = inst.Clone();
                        clone.Offset = inst.Offset;

                        if (MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var origLocal)) {
                            var local = localMap[origLocal].local;
                            switch (clone.OpCode.Code) {
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                case Code.Ldloc_S:
                                case Code.Ldloc:
                                    clone.OpCode = OpCodes.Ldloc;
                                    break;
                                case Code.Ldloca_S:
                                case Code.Ldloca:
                                    clone.OpCode = OpCodes.Ldloca;
                                    break;
                                case Code.Stloc_0:
                                case Code.Stloc_1:
                                case Code.Stloc_2:
                                case Code.Stloc_3:
                                case Code.Stloc_S:
                                case Code.Stloc:
                                    clone.OpCode = OpCodes.Stloc;
                                    break;
                            }
                            clone.Operand = local;
                        }

                        ilProcessor.InsertBefore(returnInst, clone);
                    }
                }
            }
        }


        static void HandleLoadStaticField(IJumpSitesCacheFeature feature,
            PatcherArguments arguments,
            MethodDefinition caller,
            Dictionary<string, HashSet<Instruction>> extracteds,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            Instruction inst,
            bool isAddress) {

            var fieldDef = ((FieldReference)inst.Operand).TryResolve();
            if (fieldDef is null) {
                return;
            }
            if (!arguments.InitialStaticFields.ContainsKey(fieldDef.GetIdentifier())) {
                return;
            }
            if (!extracteds.TryGetValue(fieldDef.GetIdentifier(), out var transformInsts)) {
                extracteds.Add(fieldDef.GetIdentifier(), transformInsts = []);
            }
            TrackUsage(feature, caller, fieldDef, transformInsts, localMap, inst);
            return;
        }
        static void HandleStoreLocal(IJumpSitesCacheFeature feature,
            PatcherArguments arguments,
            MethodDefinition caller,
            Dictionary<string, HashSet<Instruction>> extracteds,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            Instruction inst) {

            var local = MonoModCommon.IL.GetReferencedVariable(caller, inst);
            bool usedTransformInst = false;
            if (localMap.TryGetValue(local, out var localData)) {
                usedTransformInst = true;
            }
            else if (CheckOperandUsedTransformInst(feature, arguments, caller, extracteds, inst, local, out localData)) {
                usedTransformInst = true;
                localMap[local] = localData;
            }

            if (!usedTransformInst) {
                return;
            }

            foreach (var fieldDef in localData.fields.Values) {
                ExtractSources(feature, caller, fieldDef, extracteds[fieldDef.GetIdentifier()], localMap, inst);
            }
        }

        private static bool CheckOperandUsedTransformInst(IJumpSitesCacheFeature feature,
            PatcherArguments arguments,
            MethodDefinition caller,
            Dictionary<string, HashSet<Instruction>> extracteds,
            Instruction inst,
            VariableDefinition local,
            out (VariableDefinition local, Dictionary<string, FieldDefinition> fields) localData) {

            var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, inst, feature.GetMethodJumpSites(caller));

            localData = (new VariableDefinition(local.VariableType), new Dictionary<string, FieldDefinition>());

            foreach (var kv in extracteds) {
                if (arguments.InitialStaticFields.TryGetValue(kv.Key, out var fieldDef)) { }
                else if (arguments.ModifiedStaticFields.TryGetValue(kv.Key, out fieldDef)) { }
                else if (arguments.UnmodifiedStaticFields.TryGetValue(kv.Key, out fieldDef)) { }
                else {
                    continue;
                }

                foreach (var path in paths) {
                    foreach (var source in path.ParametersSources) {
                        foreach (var sourceInst in source.Instructions) {
                            if (kv.Value.Contains(sourceInst)) {
                                localData.fields.TryAdd(kv.Key, fieldDef);
                                goto nextLoop;
                            }
                        }
                    }
                }
            nextLoop:;
            }

            return localData.fields.Count > 0;
        }

        static void HandleStoreStaticField(IJumpSitesCacheFeature feature,
            PatcherArguments arguments,
            MethodDefinition caller,
            Dictionary<string, HashSet<Instruction>> extracteds,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            Instruction inst) {

            var fieldDef = ((FieldReference)inst.Operand).TryResolve();
            if (fieldDef is null) {
                return;
            }
            if (!arguments.InitialStaticFields.ContainsKey(fieldDef.GetIdentifier())) {
                return;
            }
            if (!extracteds.TryGetValue(fieldDef.GetIdentifier(), out var transformInsts)) {
                extracteds.Add(fieldDef.GetIdentifier(), transformInsts = []);
            }
            ExtractSources(feature, caller, fieldDef, transformInsts, localMap, inst);
            return;
        }
        static void TrackUsage(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            FieldDefinition referencedField,
            HashSet<Instruction> transformInsts,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            Instruction inst) {

            Stack<Instruction> works = [];
            works.Push(inst);

            while (works.Count > 0) {
                var current = works.Pop();
                var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(caller, current);
                ExtractSources(feature, caller, referencedField, transformInsts, localMap, usages);
                foreach (var usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(caller.Body, usage) > 0) {
                        works.Push(usage);
                    }
                }
            }
        }
        static void ExtractSources(IJumpSitesCacheFeature feature,
            MethodDefinition caller,
            FieldDefinition referenceField,
            HashSet<Instruction> transformInsts,
            Dictionary<VariableDefinition, (VariableDefinition local, Dictionary<string, FieldDefinition> fields)> localMap,
            params IEnumerable<Instruction> extractSources) {

            var jumpSite = feature.GetMethodJumpSites(caller);

            Stack<Instruction> stack = [];
            foreach (var checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                var check = stack.Pop();
                if (!transformInsts.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only pop value from stack
                else if (MonoModCommon.Stack.GetPopCount(caller.Body, check) > 0) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(caller, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }

                if (MonoModCommon.IL.TryGetReferencedVariable(caller, check, out var local)) {
                    if (localMap.TryGetValue(local, out var tuple)) {
                        tuple.fields.TryAdd(referenceField.GetIdentifier(), referenceField);
                        continue;
                    }
                    foreach (var inst in caller.Body.Instructions) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(caller, inst, out var otherLocal) || otherLocal.Index != local.Index) {
                            continue;
                        }
                        // store local
                        if (MonoModCommon.Stack.GetPopCount(caller.Body, inst) > 0) {
                            stack.Push(inst);
                        }
                        else {
                            transformInsts.Add(inst);
                        }
                    }
                    localMap.Add(local, (new VariableDefinition(local.VariableType), new() { { referenceField.GetIdentifier(), referenceField } }));
                }
            }
        }
    }
}
