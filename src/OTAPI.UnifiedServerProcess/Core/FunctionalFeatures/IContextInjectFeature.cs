using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IContextInjectFeature : IJumpSitesCacheFeature { }
    public static class ContextInjectFeatureExtensions
    {
        public static bool AdjustMethodReferences(
            this IContextInjectFeature _,
            PatcherArguments arguments,
            ContextBoundMethodMap contextMethodMap,
            ref MethodReference methodRefToAdjust,
            [NotNullWhen(true)]
            out MethodDefinition? contextBoundMethod,
            out MethodReference originalMethodRef,
            out ContextTypeData? contextProvider) {

            originalMethodRef = methodRefToAdjust;
            contextProvider = null;
            var calleeId = methodRefToAdjust.GetIdentifier();

            // Validate if the method reference points to an unmodified vanilla method through:
            // 1. Existence in original-context-bound mapping, OR
            // 2. Preservation of original state (unresolvable method indicates no patches)
            // Only valid candidates will be replaced with context-aware versions
            if (!contextMethodMap.originalToContextBound.TryGetValue(calleeId, out contextBoundMethod)) {
                // Check if method exists in tail assembly (indicates modified)
                if (methodRefToAdjust.TryResolve() is not null) {
                    return false;
                }

                // Attempt to find in context-bound registry
                if (!contextMethodMap.contextBoundMethods.TryGetValue(calleeId, out contextBoundMethod)) {
                    return false;
                }

                // Preserve original method reference for flow analysis by creating an unbound version.
                // Required because compiler-generated code bypasses static redirection logic.
                originalMethodRef = PatchingCommon.GetVanillaMethodRef(
                    arguments.RootContextDef,
                    arguments.ContextTypes,
                    methodRefToAdjust);
            }
            // If it is a reused singleton method
            // it must be called in a vanilla correctly way already, no need to create a context-bound version
            else if (arguments.ContextTypes.TryGetValue(methodRefToAdjust.DeclaringType.FullName, out contextProvider)
                && contextProvider.IsReusedSingleton
                && (contextProvider.ReusedSingletonMethods.ContainsKey(calleeId))) {
                return false;
            }
            // If it is a static method in reused singleton context type, then the context bound version is just converted from static to instance
            // So the identifier is the same, but the processed method will set HasThis to true
            // So if HasThis is true, it is a processed method, we can skip
            else if (contextBoundMethod.GetIdentifier() == calleeId && methodRefToAdjust.HasThis) {
                return false;
            }
            // Create context-aware version for vanilla method references
            else {
                methodRefToAdjust = PatchingCommon.CreateMethodReference(methodRefToAdjust, contextBoundMethod);
            }

            arguments.ContextTypes.TryGetValue(contextBoundMethod.DeclaringType.FullName, out contextProvider);
            if (contextBoundMethod.IsConstructor) {
                contextProvider = null;
            }

            return true;
        }


        public static void InjectContextParameterLoads(
            this IContextInjectFeature point,
            PatcherArguments arguments,
            ref Instruction methodCallInstruction,
            out Instruction insertedFirstInstr,
            MethodDefinition modifyMethod,
            MethodDefinition contextBound,
            MethodReference calleeRef,
            MethodReference vanillaCalleeRef,
            ContextTypeData? contextTypeData,
            Instruction[] loads) {

            TypeDefinition contextTypeDef;
            int contextParamInsertIndex;

            // Determine context type and insertion position based on method characteristics:
            if (!vanillaCalleeRef.HasThis && calleeRef.HasThis && contextTypeData is not null) {
                // Static-to-instance conversion: use declaring type's context at index 0
                contextTypeDef = contextTypeData.ContextTypeDef;
                contextParamInsertIndex = 0;
            }
            else if (!calleeRef.HasThis || (calleeRef.Name == ".ctor" && methodCallInstruction.OpCode == OpCodes.Newobj)) {
                // Static method or constructor: use root context at index 0
                contextTypeDef = arguments.RootContextDef;
                contextParamInsertIndex = 0;
            }
            else {
                // Instance method: use root context after 'this' (index 1)
                contextTypeDef = arguments.RootContextDef;
                contextParamInsertIndex = 1;
            }

            methodCallInstruction.Operand = vanillaCalleeRef; // Use vanilla for stack analysis

            var jumpSites = point.GetMethodJumpSites(modifyMethod);

            HashSet<Instruction> insertBeforeTargets = [];

            // Determine optimal insertion points for context parameters
            if ((!vanillaCalleeRef.HasThis || (vanillaCalleeRef.Name == ".ctor" && methodCallInstruction.OpCode == OpCodes.Newobj)) && vanillaCalleeRef.Parameters.Count == 0) {
                insertBeforeTargets.Add(methodCallInstruction); // Insert before call
            }
            else {
                foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(modifyMethod, methodCallInstruction, jumpSites)) {
                    if (path.ParametersSources.Length == contextParamInsertIndex) {
                        insertBeforeTargets.Add(methodCallInstruction);
                    }
                    else if (path.ParametersSources.Length > contextParamInsertIndex) {
                        insertBeforeTargets.Add(path.ParametersSources[contextParamInsertIndex].Instructions.First());
                    }
                    else {
                        throw new IndexOutOfRangeException($"Invalid context TracingParameter insert index {nameof(contextParamInsertIndex)} value");
                    }
                }
            }

            var ilProcessor = modifyMethod.Body.GetILProcessor();

            var insertBeforeTargetArray = insertBeforeTargets.ToArray();
            if (insertBeforeTargetArray.Length == 1) {
                var insertTarget = insertBeforeTargetArray[0];
                if (insertTarget == methodCallInstruction) {
                    if (methodCallInstruction.Previous is not null && methodCallInstruction.Previous.OpCode == OpCodes.Constrained) {
                        insertTarget = methodCallInstruction.Previous;
                        ilProcessor.InsertBeforeSeamlessly(ref insertTarget, out insertedFirstInstr, loads);
                    }
                    else {
                        ilProcessor.InsertBeforeSeamlessly(ref methodCallInstruction, out insertedFirstInstr, loads);
                    }
                }
                else if (insertTarget.OpCode == OpCodes.Dup || insertTarget.OpCode == OpCodes.Leave || insertTarget.OpCode == OpCodes.Leave_S) {
                    var tmpLocalType = MonoModCommon.Stack.AnalyzeStackTopType(modifyMethod, insertTarget, jumpSites);
                    var tmpLocal = new VariableDefinition(tmpLocalType);
                    modifyMethod.Body.Variables.Add(tmpLocal);

                    insertTarget = insertTarget.Next;
                    if (insertTarget == methodCallInstruction) {
                        Instruction[] inserts = [
                            MonoModCommon.IL.BuildVariableStore(modifyMethod, modifyMethod.Body, tmpLocal),
                            ..loads,
                            MonoModCommon.IL.BuildVariableLoad(modifyMethod, modifyMethod.Body, tmpLocal)
                        ];
                        if (methodCallInstruction.Previous is not null && methodCallInstruction.Previous.OpCode == OpCodes.Constrained) {
                            ilProcessor.InsertBeforeSeamlessly(ref insertTarget, out insertedFirstInstr, inserts);
                        }
                        else {
                            ilProcessor.InsertBeforeSeamlessly(ref methodCallInstruction, out insertedFirstInstr, inserts);
                        }
                    }
                    else {
                        ilProcessor.InsertBeforeSeamlessly(ref insertTarget, out insertedFirstInstr, [
                            MonoModCommon.IL.BuildVariableStore(modifyMethod, modifyMethod.Body, tmpLocal),
                            ..loads,
                            MonoModCommon.IL.BuildVariableLoad(modifyMethod, modifyMethod.Body, tmpLocal)
                        ]);
                    }
                }
                else {
                    ilProcessor.InsertBeforeSeamlessly(ref insertTarget, out insertedFirstInstr, loads);
                }
            }
            else if (MonoModCommon.Stack.CheckSinglePredecessor(modifyMethod, insertBeforeTargetArray, out var upper, out _, jumpSites)) {
                ilProcessor.InsertBeforeSeamlessly(ref upper, out insertedFirstInstr, loads);
            }
            else {
                var first = insertBeforeTargets.First();
                ilProcessor.InsertBeforeSeamlessly(ref first, out insertedFirstInstr, loads.Select(i => i.Clone()));
                foreach (var insertTarget in insertBeforeTargets.Skip(1)) {
                    var tmp = insertTarget;
                    ilProcessor.InsertBeforeSeamlessly(ref tmp, loads.Select(i => i.Clone()));
                }
            }
            if (contextTypeData is not null && contextTypeData.IsPredefined && calleeRef.Resolve().IsVirtual) {
                methodCallInstruction.OpCode = OpCodes.Callvirt;
            }
            methodCallInstruction.Operand = calleeRef; // Restore context-bound callee
        }

        public static void InjectContextFieldStoreInstanceLoads(
            this IContextInjectFeature point,
            PatcherArguments arguments,
            ref Instruction fieldStoreInstruction,
            out Instruction insertedFirstInstr,
            MethodDefinition modifyMethod,
            FieldDefinition contextBoundFieldDef,
            FieldReference origFieldRef,
            Instruction[] loads) {

            var paths = MonoModCommon.Stack.AnalyzeInstructionArgsSources(modifyMethod, fieldStoreInstruction, point.GetMethodJumpSites(modifyMethod));
            var defLoadValue = paths[0].ParametersSources[0].Instructions[0];


            var ilProcessor = modifyMethod.Body.GetILProcessor();
            if (paths.Length != 1
                || defLoadValue.OpCode == OpCodes.Dup
                || defLoadValue.OpCode == OpCodes.Leave
                || defLoadValue.OpCode == OpCodes.Leave_S) {

                VariableDefinition local;
                modifyMethod.Body.Variables.Add(local = new VariableDefinition(origFieldRef.FieldType));

                loads = [
                    MonoModCommon.IL.BuildVariableStore(modifyMethod, modifyMethod.Body, local),
                    ..loads,
                    MonoModCommon.IL.BuildVariableLoad(modifyMethod, modifyMethod.Body, local),
                ];

                ilProcessor.InsertBeforeSeamlessly(ref fieldStoreInstruction, out insertedFirstInstr, loads);
            }
            else {
                ilProcessor.InsertBeforeSeamlessly(ref defLoadValue, out insertedFirstInstr, loads);
            }
            fieldStoreInstruction.OpCode = OpCodes.Stfld;
            fieldStoreInstruction.Operand = contextBoundFieldDef;
        }
        public static void InjectContextFieldLoadInstanceLoads(
            this IContextInjectFeature point,
            PatcherArguments arguments,
            ref Instruction fieldLoadInstruction,
            out Instruction insertedFirstInstr,
            bool isAddress,
            MethodDefinition modifyMethod,
            FieldDefinition contextBoundFieldDef,
            FieldReference origFieldRef,
            Instruction[] loads) {

            var ilProcessor = modifyMethod.Body.GetILProcessor();

            if (fieldLoadInstruction.Previous is not null && fieldLoadInstruction.Previous.OpCode == OpCodes.Volatile) {
                var tmp = fieldLoadInstruction.Previous;
                ilProcessor.InsertBeforeSeamlessly(ref tmp, out insertedFirstInstr, loads);
            }
            else {
                ilProcessor.InsertBeforeSeamlessly(ref fieldLoadInstruction, out insertedFirstInstr, loads);
            }
            fieldLoadInstruction.OpCode = isAddress ? OpCodes.Ldflda : OpCodes.Ldfld;
            fieldLoadInstruction.Operand = contextBoundFieldDef;
        }
        /// <summary>
        /// <para>The field initialization can occur before calling the base class constructor or its own constructor, </para>
        /// <para>while the code that uses parameters must be executed after calling the base class constructor or its own constructor. </para>
        /// <para>This method is designed to move the call to the base class constructor or its own constructor to before the first loaded context Parameter after dependency injection.</para>
        /// </summary>
        /// <param name="point"></param>
        /// <param name="rootContextDef"></param>
        /// <param name="ctor"></param>
        public static void AdjustConstructorLoadRoot(this IContextInjectFeature point, TypeDefinition rootContextDef, MethodDefinition ctor, bool shouldMoveSelfStore) {
            if (ctor.Name != ".ctor"
                || ctor.Parameters.Count == 0
                || ctor.Parameters[0].ParameterType.FullName != rootContextDef.FullName) {
                return;
            }

            Instruction? baseCtorCall = null;
            Instruction? firstLoadRoot_shouldMoveCtorCallWhenNotNull = null;
            for (int i = 0; i < ctor.Body.Instructions.Count; i++) {
                var check = ctor.Body.Instructions[i];
                if (baseCtorCall is null && check is { OpCode.Code: Code.Call, Operand: MethodReference { Name: ".ctor" } checkCtor }) {
                    var checkCtorTypeDef = checkCtor.DeclaringType.Resolve();
                    if (checkCtorTypeDef.FullName == ctor.DeclaringType.BaseType.FullName || checkCtorTypeDef.FullName == ctor.DeclaringType.FullName) {
                        baseCtorCall = check;
                        if (firstLoadRoot_shouldMoveCtorCallWhenNotNull is null) {
                            break;
                        }
                    }
                }
                if (baseCtorCall is null && firstLoadRoot_shouldMoveCtorCallWhenNotNull is null) {
                    bool isNotInit = false;
                    if (check.OpCode == OpCodes.Ldarg_1) {
                        foreach (var usage in MonoModCommon.Stack.AnalyzeStackTopValueUsage(ctor, check)) {
                            if (usage is not { OpCode.Code: Code.Call, Operand: MethodReference { Name: ".ctor" } }) {
                                isNotInit = true;
                                break;
                            }
                        }
                    }
                    else if (check.OpCode == OpCodes.Ldarg_0) {
                        foreach (var usage in MonoModCommon.Stack.AnalyzeStackTopValueUsage(ctor, check)) {
                            if (usage.OpCode != OpCodes.Stfld && usage is not { OpCode.Code: Code.Call, Operand: MethodReference { Name: ".ctor" } }) {
                                isNotInit = true;
                                break;
                            }
                        }
                    }
                    if (isNotInit) {
                        HashSet<Instruction> checkInsts = [];
                        TraceUsage(point, ctor, checkInsts, [], check);
                        foreach (var inst in ctor.Body.Instructions) {
                            if (checkInsts.Contains(inst)) {
                                firstLoadRoot_shouldMoveCtorCallWhenNotNull = inst;
                                break;
                            }
                        }
                        firstLoadRoot_shouldMoveCtorCallWhenNotNull ??= check;
                    }
                }
            }
            if (firstLoadRoot_shouldMoveCtorCallWhenNotNull is null || baseCtorCall is null) {
                return;
            }

            var jumpSite = point.GetMethodJumpSites(ctor);

            var loadPaths = MonoModCommon.Stack.AnalyzeParametersSources(ctor, baseCtorCall, jumpSite);

            List<Instruction> movedInstructions = [];
            if (loadPaths.Length != 0 && loadPaths[0].ParametersSources.Length != 0) {
                var blockCheck = loadPaths[0].ParametersSources[0].Instructions[0];
                while (blockCheck != baseCtorCall) {
                    movedInstructions.Add(blockCheck);
                    blockCheck = blockCheck.Next;
                }
            }
            movedInstructions.Add(baseCtorCall);
            var storeSelf = ctor.Body.Instructions.FirstOrDefault(
                i =>
                i is { OpCode.Code: Code.Stfld, Operand: FieldReference field } && field.FieldType.FullName == ctor.DeclaringType.FullName);
            var storeRootField = ctor.Body.Instructions.FirstOrDefault(
                i =>
                i is { OpCode.Code: Code.Stfld, Operand: FieldReference field } && field.FieldType.FullName == rootContextDef.FullName
                && i.Previous.OpCode == OpCodes.Ldarg_1
                && i.Previous.Previous.OpCode == OpCodes.Ldarg_0);

            if (shouldMoveSelfStore && storeSelf is not null) {
                var path = MonoModCommon.Stack.AnalyzeInstructionArgsSources(ctor, storeSelf).Single();
                if (path.ParametersSources[0].Instructions[0].OpCode == OpCodes.Ldarg_1) {
                    // load parent
                    movedInstructions.AddRange(path.ParametersSources[0].Instructions);
                    // load self
                    movedInstructions.AddRange(path.ParametersSources[1].Instructions.Single());
                    movedInstructions.Add(storeSelf);
                }
            }

            if (storeRootField is not null) {
                movedInstructions.Add(storeRootField.Previous.Previous);
                movedInstructions.Add(storeRootField.Previous);
                movedInstructions.Add(storeRootField);
            }

            if (movedInstructions.Contains(firstLoadRoot_shouldMoveCtorCallWhenNotNull)) {
                return;
            }

            foreach (var instr in movedInstructions) {
                ctor.Body.RemoveInstructionSeamlessly(jumpSite, instr);
            }

            var ilProcessor = ctor.Body.GetILProcessor();
            ilProcessor.InsertBeforeSeamlessly(ref firstLoadRoot_shouldMoveCtorCallWhenNotNull, movedInstructions.Select(i => i.Clone()));
        }
        static void TraceUsage(IContextInjectFeature feature, MethodDefinition method, HashSet<Instruction> checkInsts, HashSet<VariableDefinition> checkLocals, Instruction instruction) {

            Stack<Instruction> works = [];
            if (!checkInsts.Contains(instruction)) {
                works.Push(instruction);
            }

            while (works.Count > 0) {
                var current = works.Pop();
                var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, current);
                ExtractSources(feature, method, checkInsts, checkLocals, usages);
                foreach (var usage in usages) {
                    if (MonoModCommon.Stack.GetPushCount(method.Body, usage) > 0) {
                        works.Push(usage);
                    }
                }
            }

            foreach (var inst in method.Body.Instructions) {
                if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var local)) {
                    continue;
                }
                if (!checkLocals.Contains(local)) {
                    continue;
                }
                switch (inst.OpCode.Code) {
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloca_S:
                    case Code.Ldloca:
                        if (!checkInsts.Contains(inst)) {
                            var usages = MonoModCommon.Stack.AnalyzeStackTopValueUsage(method, inst);
                            ExtractSources(feature, method, checkInsts, checkLocals, usages);
                            checkInsts.Add(inst);
                        }
                        break;
                }
            }
        }
        static void ExtractSources(IContextInjectFeature point, MethodDefinition method, HashSet<Instruction> checkInsts, HashSet<VariableDefinition> checkLocals, params IEnumerable<Instruction> extractSources) {
            var jumpSite = point.GetMethodJumpSites(method);

            Stack<Instruction> stack = [];
            foreach (var checkSource in extractSources) {
                stack.Push(checkSource);
            }
            while (stack.Count > 0) {
                var check = stack.Pop();
                if (!checkInsts.Add(check)) {
                    continue;
                }

                if (check.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeParametersSources(method, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only pop value from stack
                else if (MonoModCommon.Stack.GetPopCount(method.Body, check) > 0) {
                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, check, jumpSite)) {
                        foreach (var source in path.ParametersSources) {
                            foreach (var inst in source.Instructions) {
                                stack.Push(inst);
                            }
                        }
                    }
                }
                // Only push value to stack
                else if (MonoModCommon.IL.TryGetReferencedVariable(method, check, out var local)) {
                    if (checkLocals.Contains(local)) {
                        continue;
                    }
                    foreach (var inst in method.Body.Instructions) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var otherLocal) || otherLocal.Index != local.Index) {
                            continue;
                        }
                        // store local
                        if (MonoModCommon.Stack.GetPopCount(method.Body, inst) > 0) {
                            stack.Push(inst);
                        }
                    }
                    checkLocals.Add(local);
                }
            }
        }
    }
}
