using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NuGet.Packaging;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Analyzes and collects parts requiring contextualization from original static constructors, splits them for de-staticization and redirection, 
    /// <para>and merges the processed logic into constructors of corresponding contextualized entity classes.</para>
    /// </summary>
    /// <param name="logger"></param>
    public sealed class CctorCtxAdaptPatcher(ILogger logger) : GeneralPatcher(logger), IContextInjectFeature, IJumpSitesCacheFeature
    {
        public sealed override string Name => nameof(CctorCtxAdaptPatcher);
        public sealed override void Patch(PatcherArguments arguments) {
            var mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();
            List<MethodDefinition> cctors = [];
            foreach (var type in arguments.MainModule.GetAllTypes()) {
                if (!arguments.OriginalToContextType.ContainsKey(type.FullName)) {
                    continue;
                }
                var cctor = type.GetStaticConstructor();
                if (cctor is null) {
                    continue;
                }
                cctors.Add(cctor);
            }
            for (int progress = 0; progress < cctors.Count; progress++) {
                var cctor = cctors[progress];
                if (cctor.DeclaringType.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                Progress(progress, cctors.Count, $"Processing .cctor of: {cctor.DeclaringType.FullName}");

                var contextTypeData = arguments.OriginalToContextType[cctor.DeclaringType.FullName];
                ExtractContextRequestedCtor(arguments, mappedMethods, cctor, contextTypeData, out var newCtor);
                if (newCtor is null) {
                    continue;
                }
                ProcessNewCtor(arguments, newCtor, contextTypeData);
                UseThisInsteadRecursiveCtorCalls(arguments, newCtor, contextTypeData);
                UseRootParamInsteadRootFieldLoad(arguments, newCtor, contextTypeData);
                newCtor.Body.SimplifyMacros();
                MergeIntoExistingCtor(arguments.RootContextDef, newCtor, contextTypeData.constructor);
            }
        }
        public void ExtractContextRequestedCtor(
            PatcherArguments arguments,
            ContextBoundMethodMap mappedMethods,
            MethodDefinition cctor,
            ContextTypeData contextTypeData,
            out MethodDefinition? newCtor) {

            newCtor = null;
            HashSet<Instruction> transformInsts = [];
            Dictionary<VariableDefinition, VariableDefinition> localMap = [];
            var total = cctor.Body.Instructions.Count;
            foreach (var inst in cctor.Body.Instructions) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                    case Code.Ldsflda:
                        HandleLoadStaticField(this, arguments, cctor, transformInsts, localMap, inst, inst.OpCode == OpCodes.Ldsflda);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(this, arguments, cctor, transformInsts, localMap, inst);
                        break;
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                    case Code.Stloc_S:
                    case Code.Stloc:
                        HandleStoreLocal(this, mappedMethods, cctor, transformInsts, localMap, inst);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(this, arguments, mappedMethods, cctor, transformInsts, localMap, inst);
                        break;
                }
            }
            foreach (var inst in cctor.Body.Instructions) {
                if (!MonoModCommon.IL.TryGetReferencedVariable(cctor, inst, out var local)) {
                    continue;
                }
                if (!localMap.ContainsKey(local)) {
                    continue;
                }
                switch (inst.OpCode.Code) {
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                    case Code.Stloc_S:
                    case Code.Stloc:
                        transformInsts.Add(inst);
                        ExtractSources(this, cctor, transformInsts, localMap, inst);
                        break;
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloca_S:
                    case Code.Ldloca:
                        transformInsts.Add(inst);
                        TrackUsage(this, cctor, transformInsts, localMap, inst);
                        break;
                }
            }

            ExtractBranch(this, cctor, transformInsts, localMap);

            if (transformInsts.Count != 0) {
                newCtor = BuildNewConstructor(arguments, cctor, transformInsts, localMap);
            }
            return;

            MethodDefinition BuildNewConstructor(PatcherArguments arguments, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap) {
                var newCtor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, cctor.ReturnType);
                newCtor.Parameters.Add(new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                var newCtorBody = new MethodBody(newCtor);
                newCtor.Body = newCtorBody;
                newCtor.DeclaringType = cctor.DeclaringType;

                Dictionary<Instruction, Instruction> copiedMap = [];

                foreach (var inst in cctor.Body.Instructions.ToArray()) {
                    if (!transformInsts.Contains(inst)) {
                        continue;
                    }
                    var copiedInst = inst.Clone();
                    copiedMap.Add(inst, copiedInst);
                    if (MonoModCommon.IL.TryGetReferencedVariable(cctor, inst, out var originalLocal)) {
                        if (localMap.TryGetValue(originalLocal, out var mappedLocal)) {
                            copiedInst.Operand = mappedLocal;
                        }
                        else {
                            mappedLocal = new VariableDefinition(originalLocal.VariableType);
                            localMap.Add(originalLocal, mappedLocal);
                            copiedInst.Operand = mappedLocal;
                        }
                        switch (copiedInst.OpCode.Code) {
                            case Code.Ldloc_0:
                            case Code.Ldloc_1:
                            case Code.Ldloc_2:
                            case Code.Ldloc_3:
                            case Code.Ldloc_S:
                            case Code.Ldloc:
                                copiedInst.OpCode = OpCodes.Ldloc;
                                break;
                            case Code.Stloc_0:
                            case Code.Stloc_1:
                            case Code.Stloc_2:
                            case Code.Stloc_3:
                            case Code.Stloc_S:
                            case Code.Stloc:
                                copiedInst.OpCode = OpCodes.Stloc;
                                break;
                            case Code.Ldloca_S:
                            case Code.Ldloca:
                                copiedInst.OpCode = OpCodes.Ldloca;
                                break;
                        }
                    }
                    newCtorBody.Instructions.Add(copiedInst);
                    cctor.Body.RemoveInstructionSeamlessly(this.GetMethodJumpSites(cctor), inst);
                }

                foreach (var inst in newCtor.Body.Instructions) {
                    if (inst.Operand is Instruction originalInst) {
                        inst.Operand = copiedMap[originalInst];
                    }
                    else if (inst.Operand is Instruction[] originalInsts) {
                        inst.Operand = originalInsts.Select(c => copiedMap[c]).ToArray();
                    }
                }

                newCtorBody.Variables.AddRange(localMap.Values);

                return newCtor;
            }

            static void ExtractBranch(IJumpSitesCacheFeature feature, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap) {
                List<Instruction> extractSources = [];
                for (int i = 0; i < cctor.Body.Instructions.Count; i++) {
                    Instruction? inst = cctor.Body.Instructions[i];
                    if (inst.Operand is Instruction jumpTarget) {
                        if (transformInsts.Contains(jumpTarget)) {
                            extractSources.Add(inst);
                        }
                    }
                    else if (inst.Operand is Instruction[] jumpTargets) {
                        if (jumpTargets.Any(transformInsts.Contains)) {
                            extractSources.Add(inst);
                        }
                    }
                }
                ExtractSources(feature, cctor, transformInsts, localMap, extractSources);
            }
            static void TrackUsage(IJumpSitesCacheFeature feature, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, Instruction inst) {
                Stack<Instruction> works = [];
                works.Push(inst);

                while (works.Count > 0) {
                    var current = works.Pop();
                    var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(cctor, current);
                    ExtractSources(feature, cctor, transformInsts, localMap, usages);
                    foreach (var usage in usages) {
                        if (MonoModCommon.Stack.GetPushCount(cctor.Body, usage) > 0) {
                            works.Push(usage);
                        }
                    }
                }
            }
            static void HandleStoreStaticField(IJumpSitesCacheFeature feature, PatcherArguments arguments, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, Instruction inst) {
                var fieldDef = ((FieldReference)inst.Operand).TryResolve();
                if (fieldDef is null) {
                    return;
                }
                if (!arguments.InstanceConvdFieldOrgiMap.ContainsKey(fieldDef.GetIdentifier())) {
                    return;
                }
                ExtractSources(feature, cctor, transformInsts, localMap, inst);
                return;
            }
            static void HandleLoadStaticField(IJumpSitesCacheFeature feature, PatcherArguments arguments, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, Instruction inst, bool isAddress) {
                var fieldDef = ((FieldReference)inst.Operand).TryResolve();
                if (fieldDef is null) {
                    return;
                }
                if (!arguments.InstanceConvdFieldOrgiMap.ContainsKey(fieldDef.GetIdentifier())) {
                    return;
                }
                transformInsts.Add(inst);
                TrackUsage(feature, cctor, transformInsts, localMap, inst);
                return;
            }
            static void HandleStoreLocal(IJumpSitesCacheFeature feature, ContextBoundMethodMap mappedMethods, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, Instruction inst) {
                var local = MonoModCommon.IL.GetReferencedVariable(cctor, inst);
                bool usedTransformInst = false;
                if (localMap.ContainsKey(local)) {
                    usedTransformInst = true;
                }
                else if (CheckOperandUsedTransformInst(feature, mappedMethods, cctor, transformInsts, inst)) {
                    usedTransformInst = true;

                    localMap.Add(local, new VariableDefinition(local.VariableType));
                }

                if (!usedTransformInst) {
                    return;
                }
                ExtractSources(feature, cctor, transformInsts, localMap, inst);
            }
            static void HandleMethodCall(IJumpSitesCacheFeature feature, PatcherArguments arguments, ContextBoundMethodMap mappedMethods, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, Instruction inst) {
                var callee = (MethodReference)inst.Operand;
                if (CheckedNewContextInstance(arguments, transformInsts, inst)) { }
                else if (CheckContextBoundMethod(feature, mappedMethods, cctor, transformInsts, inst)
                    || CheckMethodUsedTransformInst(feature, mappedMethods, cctor, transformInsts, inst)) {

                    ExtractSources(feature, cctor, transformInsts, localMap, inst);
                }
                else {
                    return;
                }
                if (inst.OpCode != OpCodes.Newobj && callee.ReturnType.FullName == arguments.MainModule.TypeSystem.Void.FullName) {
                    return;
                }
                TrackUsage(feature, cctor, transformInsts, localMap, inst);
                return;



                static bool CheckContextBoundMethod(IJumpSitesCacheFeature feature, ContextBoundMethodMap mappedMethods, MethodDefinition cctor, HashSet<Instruction> transformInsts, Instruction inst) {
                    if (mappedMethods.originalToContextBound.TryGetValue(((MethodReference)inst.Operand).GetIdentifier(), out var convertedMethod)) {
                        return true;
                    }
                    return false;
                }
                static bool CheckedNewContextInstance(PatcherArguments arguments, HashSet<Instruction> transformInsts, Instruction inst) {
                    if (inst.OpCode == OpCodes.Newobj && arguments.ContextTypes.ContainsKey(((MethodReference)inst.Operand).DeclaringType.FullName)) {
                        transformInsts.Add(inst);
                        return true;
                    }
                    return false;
                }
            }
            static bool CheckMethodUsedTransformInst(IJumpSitesCacheFeature feature, ContextBoundMethodMap mappedMethods, MethodDefinition cctor, HashSet<Instruction> transformInsts, Instruction inst) {
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(cctor, inst, feature.GetMethodJumpSites(cctor))) {
                    foreach (var source in path.ParametersSources) {
                        foreach (var sourceInst in source.Instructions) {
                            if (transformInsts.Contains(sourceInst)) {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            static bool CheckOperandUsedTransformInst(IJumpSitesCacheFeature feature, ContextBoundMethodMap mappedMethods, MethodDefinition cctor, HashSet<Instruction> transformInsts, Instruction inst) {
                foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(cctor, inst, feature.GetMethodJumpSites(cctor))) {
                    foreach (var source in path.ParametersSources) {
                        foreach (var sourceInst in source.Instructions) {
                            if (transformInsts.Contains(sourceInst)) {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
        }

        static void ExtractSources(IJumpSitesCacheFeature feature, MethodDefinition cctor, HashSet<Instruction> transformInsts, Dictionary<VariableDefinition, VariableDefinition> localMap, params IEnumerable<Instruction> extractSources) {
            var jumpSite = feature.GetMethodJumpSites(cctor);

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
                    if (localMap.ContainsKey(local)) {
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
                            transformInsts.Add(inst);
                        }
                    }
                    localMap.Add(local, new VariableDefinition(local.VariableType));
                }
            }
        }

        public void ProcessNewCtor(PatcherArguments arguments, MethodDefinition newCtor, ContextTypeData contextTypeData) {
            newCtor.Name = ".ctor_placeholder";
            newCtor.DeclaringType = contextTypeData.ContextTypeDef;
            this.GetMethodJumpSites(newCtor);
            foreach (var instruction in newCtor.Body.Instructions.ToArray()) {
                switch (instruction.OpCode.Code) {
                    case Code.Ldsfld:
                        HandleLoadStaticField(instruction, newCtor, false);
                        break;
                    case Code.Ldsflda:
                        HandleLoadStaticField(instruction, newCtor, true);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(instruction, newCtor);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(instruction, newCtor);
                        break;
                }
            }
            this.ClearJumpSitesCache(newCtor);
            newCtor.Name = ".ctor";
            return;

            void HandleLoadStaticField(Instruction instruction, MethodDefinition method, bool isAddress) {
                var fieldRef = (FieldReference)instruction.Operand;
                var field = fieldRef.TryResolve();

                if (field is null) {
                    return;
                }

                FieldDefinition? instanceConvdField;
                // If the loading field is just an context, it must come from a singleton field redirection
                if (arguments.OriginalToContextType.TryGetValue(field.FieldType.FullName, out var instanceConvdType) && instanceConvdType.IsReusedSingleton) {
                    // If it is loading the field value but address, and tail method is an instance method of the context
                    // Just use 'this'
                    if (method.DeclaringType.FullName == instanceConvdType.ContextTypeDef.FullName
                        && !method.IsStatic
                        && !isAddress) {

                        instruction.OpCode = OpCodes.Ldarg_0;
                        instruction.Operand = null;
                        return;
                    }
                    // Load the field by tail: ** root context -> field 1 (context) -> ... -> field n-1 (context) -> tail field **
                    // The tail field will be loaded by existing instruction: ** isAddress ? OpCodes.Ldflda : OpCodes.Ldfld **
                    // So the instructions insert before the instruction is the load of field n-1
                    else {
                        instanceConvdField = instanceConvdType.nestedChain.Last();
                        // If instance-converted types doesn't have the type of the field n-1 (pararent instance of tail field), it means the field n-1 is root context
                        if (!arguments.ContextTypes.TryGetValue(instanceConvdField.DeclaringType.FullName, out instanceConvdType)) {
                            instanceConvdType = null;
                        }
                    }
                }
                // If the loading field is a member field of a context but context itself
                else if (arguments.InstanceConvdFieldOrgiMap.TryGetValue(field.GetIdentifier(), out instanceConvdField)) {
                    var declaringType = instanceConvdField.DeclaringType;
                    // pararent instance of tail field must be a existing context
                    instanceConvdType = arguments.ContextTypes[declaringType.FullName];
                }
                else {
                    return;
                }
                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, instanceConvdType, out _);
                this.InjectContextFieldLoadInstanceLoads(arguments, ref instruction, out _, isAddress, method, instanceConvdField, fieldRef, loadInstanceInsts);
            }

            void HandleStoreStaticField(Instruction instruction, MethodDefinition method) {
                var fieldRef = (FieldReference)instruction.Operand;
                var field = fieldRef.TryResolve();
                if (field is null) {
                    return;
                }

                FieldDefinition? instanceConvdField;
                // If the loading field is just an context, it must come from a singleton field redirection
                if (arguments.OriginalToContextType.TryGetValue(field.FieldType.FullName, out var instanceConvdType) && instanceConvdType.IsReusedSingleton) {

                    // Load the field by tail: ** root context -> field 1 (context) -> ... -> field n-1 (context) -> tail field **

                    // The tail field will be loaded by existing instruction: ** isAddress ? OpCodes.Ldflda : OpCodes.Ldfld **
                    // So the instructions insert before the instruction is the load of field n-1

                    instanceConvdField = instanceConvdType.nestedChain.Last();
                    // If instance-converted types doesn't have the type of the field n-1 (pararent instance of tail field), it means the field n-1 is root context
                    if (!arguments.ContextTypes.TryGetValue(instanceConvdField.DeclaringType.FullName, out instanceConvdType)) {
                        instanceConvdType = null;
                    }
                }
                // If the loading field is a member field of a context but context itself
                else if (arguments.InstanceConvdFieldOrgiMap.TryGetValue(field.GetIdentifier(), out instanceConvdField)) {
                    var declaringType = instanceConvdField.DeclaringType;
                    // pararent instance of tail field must be a existing context
                    instanceConvdType = arguments.ContextTypes[declaringType.FullName];
                }
                else {
                    return;
                }

                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, instanceConvdType, out _);
                this.InjectContextFieldStoreInstanceLoads(arguments, ref instruction, out _, method, instanceConvdField, fieldRef, loadInstanceInsts);
            }

            void HandleMethodCall(Instruction methodCallInstruction, MethodDefinition caller) {

                var calleeRefToAdjust = (MethodReference)methodCallInstruction.Operand;

                if (!this.AdjustMethodReferences(arguments, arguments.LoadVariable<ContextBoundMethodMap>(), ref calleeRefToAdjust, out var contextBound, out var vanillaCallee, out var contextType)) {
                    return;
                }
                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, caller.Body, contextType, out _);

                this.InjectContextParameterLoads(arguments, ref methodCallInstruction, out _, caller, contextBound, calleeRefToAdjust, vanillaCallee, contextType, loadInstanceInsts);
            }
        }
        public void UseThisInsteadRecursiveCtorCalls(PatcherArguments arguments, MethodDefinition newCtor, ContextTypeData contextTypeData) {
            Dictionary<VariableDefinition, VariableDefinition> localMap = newCtor.Body.Variables.ToDictionary(v => v, v => v);
            HashSet<Instruction> sources = [];
            List<Instruction> recursiveCtorCalls = [];
            foreach (var instruction in newCtor.Body.Instructions) {
                if (instruction.OpCode != OpCodes.Newobj) {
                    continue;
                }
                var calleeRef = (MethodReference)instruction.Operand;
                if (calleeRef.DeclaringType.FullName != newCtor.DeclaringType.FullName) {
                    continue;
                }
                ExtractSources(this, newCtor, sources, localMap, instruction);
                recursiveCtorCalls.Add(instruction);
            }
            if (recursiveCtorCalls.Count == 0) {
                return;
            }
            contextTypeData.SingletonCtorCallShouldBeMoveToRootCtor = true;
            foreach (var recursiveCtorCall in recursiveCtorCalls) {
                sources.Remove(recursiveCtorCall);
                recursiveCtorCall.OpCode = OpCodes.Ldarg_0;
                recursiveCtorCall.Operand = null;
            }
            foreach (var source in sources) {
                newCtor.Body.Instructions.Remove(source);
            }
        }
        public void UseRootParamInsteadRootFieldLoad(PatcherArguments arguments, MethodDefinition newCtor, ContextTypeData contextTypeData) {
            Dictionary<VariableDefinition, VariableDefinition> localMap = newCtor.Body.Variables.ToDictionary(v => v, v => v);
            HashSet<Instruction> sources = [];
            List<Instruction> rootFieldLoads = [];
            foreach (var instruction in newCtor.Body.Instructions) {
                if (instruction.OpCode != OpCodes.Ldfld
                    || instruction.Operand is not FieldReference fieldRef
                    || fieldRef.FieldType.FullName != arguments.RootContextDef.FullName) {
                    continue;
                }
                ExtractSources(this, newCtor, sources, localMap, instruction);
                rootFieldLoads.Add(instruction);
            }
            foreach (var rootFieldLoad in rootFieldLoads) {
                sources.Remove(rootFieldLoad);
                rootFieldLoad.OpCode = OpCodes.Ldarg_1;
                rootFieldLoad.Operand = null;
            }
            foreach (var source in sources) {
                newCtor.Body.Instructions.Remove(source);
            }
        }
        static void MergeIntoExistingCtor(TypeDefinition rootContextDef, MethodDefinition newCtor, MethodDefinition existingCtor) {
            var insertBefore = existingCtor.Body.Instructions.Single(
                inst => inst.OpCode == OpCodes.Stfld && ((FieldReference)inst.Operand).FieldType.FullName == rootContextDef.FullName).Next;
            existingCtor.Body.Variables.AddRange(newCtor.Body.Variables);

            var ilProcessor = existingCtor.Body.GetILProcessor();
            ilProcessor.InsertBeforeSeamlessly(ref insertBefore, newCtor.Body.Instructions);
        }
    }
}
