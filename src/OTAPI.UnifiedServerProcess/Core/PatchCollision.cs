using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace OTAPI.UnifiedServerProcess.Core
{
    public class PatchCollision(ModuleDefinition module)
    {
        static readonly HashSet<string> tlsFields = [
            nameof(Collision._cacheForConveyorBelts),
            nameof(Collision.contacts),
        ];

        static bool IsTargetCollisionField(FieldReference fieldRef) {
            return
                fieldRef.DeclaringType.FullName == "Terraria.Collision" &&
                fieldRef.Name != nameof(Collision.Epsilon) &&
                !tlsFields.Contains(fieldRef.Name);
        }
        public void Patch() {

            // Key: MethodReference identifier. Value: analysis + rewrite plan for that method.
            Dictionary<string, MethodWithPreparedVariables> collisionStateMethodsById = [];

            // Get the type definition for Terraria.Collision
            TypeDefinition collisionType = module.GetType("Terraria.Collision");

            // Seed: methods inside Terraria.Collision that assign to Collision static fields.
            foreach (MethodDefinition? method in collisionType.Methods.Where(m => m.Name != ".cctor" && m.Name != ".ctor")) {
                Instruction[] instructionSnapshot = method.Body.Instructions.ToArray();
                foreach (Instruction? instruction in instructionSnapshot) {
                    if (instruction.Operand is FieldReference setField && setField.DeclaringType.FullName is "Terraria.Collision") {

                        if (instruction.OpCode == OpCodes.Stsfld && !collisionStateMethodsById.TryGetValue(method.GetIdentifier(), out MethodWithPreparedVariables? methodData)) {
                            methodData = new MethodWithPreparedVariables(method);
                            methodData.PrepareVariables(collisionStateMethodsById);
                            break;
                        }


                    }
                }
            }

            foreach (FieldDefinition? fieldDef in collisionType.Fields) {
                if (tlsFields.Contains(fieldDef.Name) && !fieldDef.CustomAttributes.Any(a => a.AttributeType.Name is "ThreadStaticAttribute")) {
                    fieldDef.CustomAttributes.Add(new CustomAttribute(
                        new MethodReference(
                            ".ctor",
                            module.TypeSystem.Void,
                            new TypeReference("System", "ThreadStaticAttribute", module, module.TypeSystem.CoreLibrary)) {
                            HasThis = true
                        }
                    ));
                }
            }

            Console.WriteLine($"Collision static field writers found: {collisionStateMethodsById.Count}");
            foreach (MethodWithPreparedVariables m in collisionStateMethodsById.Values) {
                Console.WriteLine($"    【{m.Method.GetDebugName()}】 | {string.Join(",", m.VariablesByFieldName.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();


            // Second seed: all methods across the module that *read* Collision static fields.
            Dictionary<string, MethodWithPreparedVariables> collisionFieldReadersById = [];

            foreach (TypeDefinition? type in module.Types) {
                foreach (MethodDefinition? method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }

                    Instruction[] instructionSnapshot = method.Body.Instructions.ToArray();
                    foreach (Instruction? instruction in instructionSnapshot) {
                        if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference getField && IsTargetCollisionField(getField)) {

                            if (!collisionStateMethodsById.TryGetValue(method.GetIdentifier(), out MethodWithPreparedVariables? added)) {
                                added = new MethodWithPreparedVariables(method);
                                added.PrepareVariables(collisionStateMethodsById);
                            }
                            collisionFieldReadersById.TryAdd(method.GetIdentifier(), added);

                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Collision static field readers found: {collisionFieldReadersById.Count}");
            foreach (KeyValuePair<string, MethodWithPreparedVariables> kv in collisionFieldReadersById) {
                MethodWithPreparedVariables m = kv.Value;
                Console.WriteLine($"    【{m.Method.GetDebugName()}】 | {string.Join(",", m.VariablesByFieldName.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            // Expand callers via a shallow call graph walk (bounded to keep runtime reasonable).
            Dictionary<string, MethodWithPreparedVariables> previousDepthMethodsById = new(collisionStateMethodsById);
            Dictionary<string, MethodWithPreparedVariables> currentDepthMethodsById = [];


            for (int depthIndex = 0; depthIndex < 6; depthIndex++) {
                var callDepth = depthIndex + 2;
                // Iterate through each type in the module
                foreach (TypeDefinition? type in module.Types) {
                    // Iterate through each method in the type
                    foreach (MethodDefinition? method in type.Methods) {
                        if (!method.HasBody) {
                            continue;
                        }

                        bool callsPreviousDepthMethod = false;

                        Instruction[] instructionSnapshot = method.Body.Instructions.ToArray();
                        foreach (Instruction? instruction in instructionSnapshot) {
                            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {

                                if (previousDepthMethodsById.TryGetValue(methodRef.GetIdentifier(), out MethodWithPreparedVariables _)) {
                                    Console.WriteLine($"[Call depth {callDepth}] {method.GetDebugName()} calls previous-depth method {methodRef.GetDebugName()}");
                                    callsPreviousDepthMethod = true;
                                }
                            }
                        }

                        if (callsPreviousDepthMethod) {
                            if (!collisionStateMethodsById.TryGetValue(method.GetIdentifier(), out MethodWithPreparedVariables? added)) {
                                added = new MethodWithPreparedVariables(method);
                                added.PrepareVariables(collisionStateMethodsById);
                            }
                            else {
                                added.PrepareVariables(collisionStateMethodsById);
                            }
                            currentDepthMethodsById.TryAdd(method.GetIdentifier(), added);
                        }
                    }
                }

                Console.WriteLine($"[Call depth {callDepth}] caller methods found: {currentDepthMethodsById.Count} (【Method】 | [variables])");
                foreach (MethodWithPreparedVariables m in currentDepthMethodsById.Values) {
                    Console.WriteLine($"    【{m.Method.GetDebugName()}】| {string.Join(",", m.VariablesByFieldName.Values)}");
                }
                //Console.WriteLine($"Press Enter to continue");
                //Console.ReadLine();

                previousDepthMethodsById = new(currentDepthMethodsById);
                currentDepthMethodsById.Clear();
            }

            Dictionary<string, MethodWithPreparedVariables> methodsRequiringInputParamsById =
                collisionStateMethodsById.Values
                    .Where(m => m.VariablesByFieldName.Values.Any(v => v.Mode == VariableMode.InParam))
                    .ToDictionary(x => x.Identifier, x => x);

            foreach (MethodWithPreparedVariables? methodData in collisionStateMethodsById.Values.ToArray()) {
                // Names of Collision fields whose values are required later in this method body.
                HashSet<string> pendingRequiredStateNames = [];

                Instruction[] reverseInstructions = methodData.Method.Body.Instructions.Reverse().ToArray();
                foreach (Instruction? instruction in reverseInstructions) {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {
                        if (methodsRequiringInputParamsById.TryGetValue(methodRef.GetIdentifier(), out MethodWithPreparedVariables? calleeRequiringInputs)) {
                            foreach (VariableItem? item in calleeRequiringInputs.VariablesByFieldName.Values.Where(
                                v => v.Mode == VariableMode.InParam && v.FieldName is not nameof(Collision.shimmer) or nameof(Collision.honey))) {
                                pendingRequiredStateNames.Add(item.FieldName);
                            }
                            Console.WriteLine($"Detected that {methodData.Method.GetDebugName()} calls {methodRef.GetDebugName()} which requires inputs: {string.Join(",", pendingRequiredStateNames)}");
                        }
                        if (collisionStateMethodsById.TryGetValue(methodRef.GetIdentifier(), out MethodWithPreparedVariables? calleeMethodData)) {
                            VariableItem[] transferableOutParams = calleeMethodData.VariablesByFieldName.Values
                                .Where(v => pendingRequiredStateNames.Contains(v.FieldName) && v.Mode != VariableMode.InParam)
                                .ToArray();

                            calleeMethodData.PrepareVariables(collisionStateMethodsById, [.. transferableOutParams.Select(v => v.Field.Name)]);
                            foreach (VariableItem? item in transferableOutParams) {
                                pendingRequiredStateNames.Remove(item.FieldName);
                                Console.WriteLine($"    Promote {item.FieldName} to out-param for {calleeMethodData.Method.GetDebugName()} (needed later in {methodData.Method.GetDebugName()})");
                                Console.WriteLine($"    Pending state values: {string.Join(",", pendingRequiredStateNames)}");
                            }
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && IsTargetCollisionField(field)) {

                        pendingRequiredStateNames.Add(field.Name);
                        Console.WriteLine($"Detected that {methodData.Method.GetDebugName()} reads Collision.{field.Name}");
                    }
                }
            }

            HashSet<string> outParamPreferenceMethodIds = [
                MonoModCommon.Reference.Method(() => Collision.SlopeCollision(default,default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.noSlopeCollision(default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.TileCollision(default,default,default,default,default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.AdvancedTileCollision(default,default,default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => default(Player)!.SlopingCollision(default,default)).GetSimpleIdentifier(),
            ];

            foreach (MethodWithPreparedVariables? m in collisionStateMethodsById.Values.ToArray()) {
                if (outParamPreferenceMethodIds.Contains(m.Identifier)) {
                    m.PrepareVariables(collisionStateMethodsById, [.. m.VariablesByFieldName.Values.Where(v => v.Mode is not VariableMode.InParam).Select(v => v.Field.Name)]);
                }
                else {
                    m.PrepareVariables(collisionStateMethodsById);
                }
            }

            foreach (MethodWithPreparedVariables? m in collisionStateMethodsById.Values.ToArray()) {
                m.PrepareVariables(collisionStateMethodsById);
            }

            // Methods that require signature changes (i.e. they need in/out params, not just locals).
            List<MethodWithPreparedVariables> signatureRewriteQueue =
                [.. collisionStateMethodsById.Values.Where(m => m.VariablesByFieldName.Values.Any(v => v.Mode != VariableMode.Local))];
            Dictionary<string, MethodWithPreparedVariables> methodsToRewriteById =
                new(signatureRewriteQueue.ToDictionary(l => l.Identifier, l => l));


            Console.WriteLine($"Analysis complete: {collisionStateMethodsById.Count} related methods; {signatureRewriteQueue.Count} require signature rewrites:");
            foreach (MethodWithPreparedVariables m in signatureRewriteQueue) {
                Console.WriteLine($"    【 {m.Method.GetDebugName()} 】 | {string.Join(",", m.VariablesByFieldName.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            //Avoid infinite recursion
            HashSet<string> processedQueueMethodIds = [];
            int iteration = 1;
            while (signatureRewriteQueue.Count > 0) {
                iteration++;
                Dictionary<string, MethodWithPreparedVariables> processedThisIterationById = [];
                MethodWithPreparedVariables[] queueSnapshot = signatureRewriteQueue.ToArray();
                foreach (MethodWithPreparedVariables queuedMethod in queueSnapshot) {
                    foreach (KeyValuePair<string, MethodWithPreparedVariables> kv in collisionStateMethodsById) {
                        MethodWithPreparedVariables candidateCaller = kv.Value;
                        var candidateCallerId = kv.Key;

                        // If this candidate calls a method that needs signature rewrites, this candidate must
                        // also be rewritten so it can pass state variables along (even if it does not touch statics itself).
                        if (candidateCaller.CalleesById.ContainsKey(queuedMethod.Identifier)) {
                            Console.WriteLine($"Detected caller {candidateCaller.Method.GetDebugName()} -> non-local method {queuedMethod.Method.GetDebugName()}; mark caller as involved");

                            methodsToRewriteById.TryAdd(candidateCallerId, candidateCaller);

                            if (!processedQueueMethodIds.Contains(candidateCaller.Identifier) && candidateCaller.VariablesByFieldName.Values.Any(v => v.Mode != VariableMode.Local)) {
                                Console.WriteLine($"    Caller {candidateCaller.Method.GetDebugName()} also has non-local variables; enqueue");
                                signatureRewriteQueue.Add(candidateCaller);
                            }

                            processedThisIterationById.TryAdd(queuedMethod.Identifier, queuedMethod);
                            processedQueueMethodIds.Add(queuedMethod.Identifier);
                            signatureRewriteQueue.Remove(queuedMethod);
                        }
                    }
                }

                Console.WriteLine($"[Iteration {iteration}] processed non-local methods: {processedThisIterationById.Count}, remaining {signatureRewriteQueue.Count}");
                foreach (MethodWithPreparedVariables m in processedThisIterationById.Values) {
                    Console.WriteLine($"    【 {m.Method.GetDebugName()} 】 | {string.Join(",", m.VariablesByFieldName.Values)}");
                }

                if (signatureRewriteQueue.Count > 0) {
                    //Console.WriteLine($"Press Enter to continue");
                    //Console.ReadLine();
                }
            }


            Console.WriteLine($"Methods involved in rewrite: {methodsToRewriteById.Count}");
            foreach (MethodWithPreparedVariables methodData in methodsToRewriteById.Values) {
                methodData.ApplyVariables();
                Console.WriteLine($"    【 {methodData.Method.GetDebugName()} 】 | {string.Join(",", methodData.VariablesByFieldName.Values)}");
                Console.WriteLine($"        【params】{string.Join(",", methodData.Method.Parameters.Where(p => methodData.VariablesByFieldName.Values.Any(nv => nv.IsSameVariableReference(p))).Select(v => v.Index))}");
                Console.WriteLine($"        【locals】{string.Join(",", methodData.Method.Body.Variables.Where(v => methodData.VariablesByFieldName.Values.Any(nv => nv.IsSameVariableReference(v))).Select(v => v.Index))}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            Console.WriteLine($"Rewriting IL for involved methods...");
            foreach (MethodWithPreparedVariables methodToRewrite in methodsToRewriteById.Values) {

                //if (methodToRewrite.VariablesByFieldName.Values.Any(v => v.Mode is not VariableMode.Local)) {
                //    foreach (var param in methodToRewrite.Method.Parameters) {
                //        param.IsOptional = false;
                //    }
                //}

                ILProcessor ilProcessor = methodToRewrite.Method.Body.GetILProcessor();

                // Initialize out-params at method entry to keep callers from observing uninitialized data.
                foreach (VariableItem? outParam in methodToRewrite.VariablesByFieldName.Values.Where(v => v.Mode == VariableMode.OutParam)) {
                    Instruction methodEntryInstruction = methodToRewrite.Method.Body.Instructions.First();

                    foreach (Instruction instruction in outParam.CreateSetValueFromStackInstructions(methodToRewrite.Method, [ilProcessor.Create(OpCodes.Ldc_I4_0)])) {
                        ilProcessor.InsertBefore(methodEntryInstruction, instruction);
                    }
                }

                Instruction[] instructionSnapshot = methodToRewrite.Method.Body.Instructions.ToArray();
                foreach (Instruction? instruction in instructionSnapshot) {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {

                        var calleeId = methodRef.GetIdentifier();
                        if (methodsToRewriteById.TryGetValue(calleeId, out MethodWithPreparedVariables? calleeMethodData)) {

                            Instruction? insertBefore = null;
                            if (calleeMethodData.AnyOptionalParameter) {
                                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paths = MonoModCommon.Stack.AnalyzeParametersSources(methodToRewrite.Method, instruction, methodToRewrite.JumpSitesCache);
                                if (!paths.BeginAtSameInstruction(out _)) {
                                    throw new InvalidOperationException($"Cannot handle multiple parameter source paths for optional parameter at call to {methodRef.GetDebugName()} in {methodToRewrite.Method.GetDebugName()}");
                                }
                                insertBefore = paths
                                    .Select(path => {
                                        Instruction inst = path.ParametersSources[calleeMethodData.NonOptionalParameterCount + (methodRef.HasThis ? 1 : 0)].Instructions.First();
                                        var index = methodToRewrite.Method.Body.Instructions.IndexOf(inst);
                                        if (index < 0) {
                                            throw new Exception();
                                        }
                                        return (inst, index);
                                    })
                                    .OrderBy(t => t.index)
                                    .First()
                                    .inst;
                            }
                            else {
                                insertBefore = instruction;
                            }


                            List<VariableItem> insertedVariables = [];

                            foreach (VariableItem variable in calleeMethodData.VariablesByFieldName.Values) {

                                if (methodToRewrite.VariablesByFieldName.TryGetValue(variable.FieldName, out VariableItem? item)) {
                                    if (variable.Mode == VariableMode.InParam) {
                                        insertedVariables.Add(item);
                                        foreach (Instruction inst in item.CreatePushValueInstructions(methodToRewrite.Method)) {
                                            ilProcessor.InsertBefore(insertBefore, inst);
                                        }
                                    }
                                    else if (variable.Mode == VariableMode.OutParam) {
                                        insertedVariables.Add(item);
                                        foreach (Instruction inst in item.CreatePushAddressInstructions(methodToRewrite.Method)) {
                                            ilProcessor.InsertBefore(insertBefore, inst);
                                        }
                                    }
                                    else {
                                        Console.WriteLine($"    【{methodToRewrite.Method.GetDebugName()}】Skip {methodRef.GetDebugName()} variable: {variable}");
                                    }
                                }
                                else {
                                    Console.WriteLine($"    【{methodToRewrite.Method.GetDebugName()}】Missing variable for {methodRef.GetDebugName()}: {variable}");
                                }
                            }

                            if (insertedVariables.Count > 0) {
                                Console.WriteLine($"    【{methodToRewrite.Method.GetDebugName()}】Updated call to {methodRef.GetDebugName()}, add args: {string.Join(",", insertedVariables)}");
                            }
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference readField && IsTargetCollisionField(readField)) {

                        if (methodToRewrite.VariablesByFieldName.TryGetValue(readField.Name, out VariableItem? item)) {
                            Instruction[] replacementInstructions = item.CreatePushValueInstructions(methodToRewrite.Method);

                            // Update any instruction operands (including branch labels) that point at the old instruction.
                            foreach (Instruction? i in instructionSnapshot) {
                                if (i.Operand == instruction) {
                                    i.Operand = replacementInstructions[0];
                                }
                                if (i.Operand is ILLabel label && label.Target == instruction) {
                                    label.Target = replacementInstructions[0];
                                }
                            }

                            Instruction[] rmInstructions = [instruction];
                            int[] rmIndex = [.. rmInstructions.Select(ilProcessor.Body.Instructions.IndexOf)];

                            ilProcessor.InsertAfter(instruction, replacementInstructions.AsEnumerable());
                            foreach (var index in rmIndex) {
                                ilProcessor.Body.Instructions.RemoveAt(index);
                            }

                            Console.WriteLine($"    【{methodToRewrite.Method.GetDebugName()}】Replaced read of Collision.{readField.Name} with {item}");
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Stsfld &&
                        instruction.Operand is FieldReference writeField &&
                        writeField.DeclaringType.FullName == "Terraria.Collision") {

                        if (methodToRewrite.VariablesByFieldName.TryGetValue(writeField.Name, out VariableItem? item) && item.Mode == VariableMode.OutParam) {

                            Instruction[] replacementInstructions = item.CreateSetValueFromStackInstructions(methodToRewrite.Method, [instruction.Previous]);

                            foreach (Instruction? i in instructionSnapshot) {
                                if (i.Operand == instruction.Previous) {
                                    i.Operand = replacementInstructions[0];
                                }
                                if (i.Operand is ILLabel label && label.Target == instruction.Previous) {
                                    label.Target = replacementInstructions[0];
                                }
                            }

                            Instruction[] rmInstructions = [instruction.Previous, instruction];
                            int[] rmIndex = [.. rmInstructions.Select(ilProcessor.Body.Instructions.IndexOf)];

                            ilProcessor.InsertAfter(instruction, replacementInstructions.AsEnumerable());
                            // Must use reverse order, otherwise the index of the front will be removed first, resulting in the index of the back - 1
                            foreach (var index in rmIndex.OrderByDescending(x => x)) {
                                ilProcessor.Body.Instructions.RemoveAt(index);
                            }

                            Console.WriteLine($"    【{methodToRewrite.Method.GetDebugName()}】Replaced write to Collision.{writeField.Name} with {item}");
                        }
                    }
                }

                Console.WriteLine($"【{methodToRewrite.Method.GetDebugName()}】Rewrite completed");
            }

            Console.WriteLine($"Rewriting call sites to point at updated method signatures...");
            int replacedCallCount = 0;
            foreach (TypeDefinition? type in module.Types) {
                foreach (MethodDefinition? method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (Instruction? instruction in method.Body.Instructions) {
                        if (instruction.Operand is MethodReference methodRef && methodsToRewriteById.TryGetValue(methodRef.GetIdentifier(), out MethodWithPreparedVariables? newMethod)) {
                            instruction.Operand = MonoModCommon.Structure.DeepMapMethodReference(newMethod.Method, new());
                            replacedCallCount++;
                        }
                    }
                }
            }
            Console.WriteLine($"Replaced {replacedCallCount} method calls");

            // If a method was wrapped with a delegate helper (mfwh_...), ensure the delegate Invoke/BeginInvoke signature matches.
            foreach (MethodWithPreparedVariables m in methodsToRewriteById.Values) {
                if (!m.Method.DeclaringType.Methods.Any(other => other.Name == "mfwh_" + m.Method.Name)) {
                    continue;
                }

                foreach (Instruction? inst in m.Method.Body.Instructions) {
                    if (inst.Operand is not MethodReference { Name: ".ctor" } deleCtor) {
                        continue;
                    }
                    TypeDefinition delegateDef = deleCtor.DeclaringType.Resolve();
                    if (!delegateDef.IsDelegate()) {
                        continue;
                    }
                    MethodDefinition invokeDef = delegateDef.GetMethod("Invoke");
                    MethodDefinition beginInvoke = delegateDef.GetMethod("BeginInvoke");
                    invokeDef.Parameters.Clear();
                    ParameterDefinition asyncCallback = beginInvoke.Parameters[^2];
                    ParameterDefinition asyncState = beginInvoke.Parameters[^1];
                    beginInvoke.Parameters.Clear();
                    for (int i = 0; i < m.Method.Parameters.Count; i++) {
                        invokeDef.Parameters.Add(m.Method.Parameters[i].Clone());
                        beginInvoke.Parameters.Add(m.Method.Parameters[i].Clone());
                    }
                    beginInvoke.Parameters.Add(asyncCallback);
                    beginInvoke.Parameters.Add(asyncState);
                }
            }
        }


        public class VariableItem
        {
            public override string ToString() {
                return
                    Mode switch {
                        VariableMode.Local => $"[local {FieldName}]",
                        VariableMode.OutParam => $"[out {FieldName}]",
                        VariableMode.InParam => $"[in {FieldName}]",
                        _ => throw new NotImplementedException(),
                    };
            }

            // The Terraria.Collision static field name this variable represents.
            public readonly string FieldName;

            // Resolved field definition for that Terraria.Collision static field.
            public readonly FieldDefinition Field;

            // Exactly one of Local/Parameter is non-null depending on Mode.
            public readonly VariableDefinition? Local;
            public readonly ParameterDefinition? Parameter;
            public readonly VariableMode Mode;

            public int GetIndex(MethodDefinition method) {
                if (Local is not null) {
                    return method.Body.Variables.IndexOf(Local);
                }
                else if (Parameter is not null) {
                    return (method.HasThis ? 1 : 0) + method.Parameters.IndexOf(Parameter);
                }
                else {
                    throw new NotImplementedException();
                }
            }
            public VariableItem(FieldDefinition field, VariableDefinition variable) {
                FieldName = field.Name;
                Field = field;
                Local = variable;
                Mode = VariableMode.Local;
            }
            public VariableItem(FieldDefinition field, ParameterDefinition parameter) {
                FieldName = field.Name;
                Field = field;
                Parameter = parameter;
                if (parameter.IsOut) {
                    Mode = VariableMode.OutParam;
                }
                else {
                    Mode = VariableMode.InParam;
                }
            }
            public bool IsSameVariableReference(ParameterDefinition parameter) {
                if (Parameter is null) {
                    return false;
                }
                return parameter == Parameter;
            }
            public bool IsSameVariableReference(VariableDefinition variable) {
                if (Local is null) {
                    return false;
                }
                return variable == Local;
            }

            public void ApplyMethodVariables(MethodDefinition method) {
                if (Local is not null) {
                    if (!method.Body.Variables.Contains(Local)) {
                        method.Body.Variables.Add(Local);
                    }
                }
                else if (Parameter is not null) {
                    if (!method.Parameters.Contains(Parameter)) {
                        int count = 0;
                        for (; count < method.Parameters.Count; count++) {
                            if (method.Parameters[count].IsOptional) {
                                break;
                            }
                        }
                        PatchingCommon.InsertParamAndRemapIndices(method.Body, count, Parameter);
                    }
                }
            }
            public Instruction[] CreatePushValueInstructions(MethodDefinition method) {
                var index = GetIndex(method);
                if (index is -1) {
                    throw new InvalidOperationException();
                }
                if (Local is not null) {
                    var inst = Instruction.Create(OpCodes.Ldloc, Local);
                    if (index <= byte.MaxValue) {
                        inst.OpCode = index switch {
                            0 => OpCodes.Ldloc_0,
                            1 => OpCodes.Ldloc_1,
                            2 => OpCodes.Ldloc_2,
                            3 => OpCodes.Ldloc_3,
                            _ => OpCodes.Ldloc_S,
                        };
                        if (index <= 3) {
                            inst.Operand = null;
                        }
                    }
                    return [inst];
                }
                else if (Parameter is not null) {
                    var inst = Instruction.Create(OpCodes.Ldarg, Parameter);
                    if (index <= byte.MaxValue) {
                        inst.OpCode = index switch {
                            0 => OpCodes.Ldarg_0,
                            1 => OpCodes.Ldarg_1,
                            2 => OpCodes.Ldarg_2,
                            3 => OpCodes.Ldarg_3,
                            _ => OpCodes.Ldarg_S,
                        };
                        if (index <= 3) {
                            inst.Operand = null;
                        }
                    }
                    if (Mode == VariableMode.OutParam) {
                        OpCode opCode;
                        if (Field.FieldType.Name == typeof(int).Name) {
                            opCode = OpCodes.Ldind_I4;
                        }
                        else if (Field.FieldType.Name == typeof(bool).Name) {
                            opCode = OpCodes.Ldind_U1;
                        }
                        else {
                            throw new NotImplementedException($"Unknown type {Field.FieldType.Name}");
                        }
                        var getValue = Instruction.Create(opCode);
                        return [inst, getValue];
                    }
                    else {
                        return [inst];
                    }
                }
                else {
                    throw new InvalidOperationException();
                }
            }
            public Instruction[] CreatePushAddressInstructions(MethodDefinition method) {
                var index = GetIndex(method);
                if (index is -1) {
                    throw new InvalidOperationException();
                }
                if (Local is not null) {
                    var inst = Instruction.Create(OpCodes.Ldloca, Local);
                    if (index <= byte.MaxValue) {
                        inst.OpCode = OpCodes.Ldloca_S;
                    }
                    return [inst];
                }
                else if (Parameter is not null) {
                    if (Mode == VariableMode.OutParam) {
                        var inst = Instruction.Create(OpCodes.Ldarg, Parameter);
                        if (index <= byte.MaxValue) {
                            inst.OpCode = index switch {
                                0 => OpCodes.Ldarg_0,
                                1 => OpCodes.Ldarg_1,
                                2 => OpCodes.Ldarg_2,
                                3 => OpCodes.Ldarg_3,
                                _ => OpCodes.Ldarg_S,
                            };
                            if (index <= 3) {
                                inst.Operand = null;
                            }
                        }
                        return [inst];
                    }
                    else {
                        var inst = Instruction.Create(OpCodes.Ldarga, Parameter);
                        if (index <= byte.MaxValue) {
                            inst.OpCode = OpCodes.Ldarga_S;
                        }
                        return [inst];
                    }
                }
                else {
                    throw new InvalidOperationException();
                }
            }
            public Instruction[] CreateSetValueFromStackInstructions(MethodDefinition method, Instruction[] valueOnStack) {
                var index = GetIndex(method);
                if (index is -1) {
                    throw new InvalidOperationException();
                }
                if (Local is not null) {
                    var inst = Instruction.Create(OpCodes.Stloc, Local);
                    if (index <= byte.MaxValue) {
                        inst.OpCode = index switch {
                            0 => OpCodes.Stloc_0,
                            1 => OpCodes.Stloc_1,
                            2 => OpCodes.Stloc_2,
                            3 => OpCodes.Stloc_3,
                            _ => OpCodes.Stloc_S,
                        };
                        if (index <= 3) {
                            inst.Operand = null;
                        }
                    }
                    return [.. valueOnStack, inst];
                }
                else if (Parameter is not null) {
                    if (Mode == VariableMode.OutParam) {
                        var inst = Instruction.Create(OpCodes.Ldarg, Parameter);
                        if (index <= byte.MaxValue) {
                            inst.OpCode = index switch {
                                0 => OpCodes.Ldarg_0,
                                1 => OpCodes.Ldarg_1,
                                2 => OpCodes.Ldarg_2,
                                3 => OpCodes.Ldarg_3,
                                _ => OpCodes.Ldarg_S,
                            };
                            if (index <= 3) {
                                inst.Operand = null;
                            }
                        }
                        OpCode opCode;
                        if (Field.FieldType.Name == typeof(int).Name) {
                            opCode = OpCodes.Stind_I4;
                        }
                        else if (Field.FieldType.Name == typeof(bool).Name) {
                            opCode = OpCodes.Stind_I1;
                        }
                        else {
                            throw new NotImplementedException($"Unknown type {Field.FieldType.Name}");
                        }
                        var setValueToAddress = Instruction.Create(opCode);
                        return [inst, .. valueOnStack, setValueToAddress];
                    }
                    else {
                        var inst = Instruction.Create(OpCodes.Starg, Parameter);
                        if (index <= byte.MaxValue) {
                            inst.OpCode = OpCodes.Starg_S;
                        }
                        return [.. valueOnStack, inst];
                    }
                }
                else {
                    throw new InvalidOperationException();
                }
            }
        }
        public enum VariableMode
        {
            InParam,
            Local,
            OutParam,
        }
        public class MethodWithPreparedVariables
        {
            public readonly string Identifier;
            public readonly MethodDefinition Method;
            public readonly int NonOptionalParameterCount;
            public readonly bool AnyOptionalParameter;

            // Key: Collision static field name (e.g. "up", "down", ...)
            public readonly Dictionary<string, VariableItem> VariablesByFieldName = [];

            // Key: method identifier of a callee that is also in the analysis set.
            public readonly Dictionary<string, MethodWithPreparedVariables> CalleesById = [];

            private readonly HashSet<string> _preferredOutParamFieldNames = [];

            public readonly Dictionary<Instruction, List<Instruction>> JumpSitesCache;

            public MethodWithPreparedVariables(MethodDefinition method) {
                Identifier = method.GetIdentifier();
                Method = method;
                int count = 0;
                for (; count < method.Parameters.Count; count++) {
                    if (method.Parameters[count].IsOptional) {
                        AnyOptionalParameter = true;
                        break;
                    }
                }
                NonOptionalParameterCount = count;
                JumpSitesCache = MonoModCommon.Stack.BuildJumpSitesMap(method);
            }
            public void ApplyVariables() {
                foreach (VariableItem variable in VariablesByFieldName.Values) {
                    variable.ApplyMethodVariables(Method);
                }
            }
            public void PrepareVariables(Dictionary<string, MethodWithPreparedVariables> collection, params string[] shouldChangeToOutParam) {
                foreach (var item in shouldChangeToOutParam) {
                    _preferredOutParamFieldNames.Add(item);
                }

                // First, look for direct field writes, this priority is highest
                foreach (Instruction? instruction in Method.Body.Instructions) {
                    if (instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference field && IsTargetCollisionField(field)) {
                        _preferredOutParamFieldNames.Add(field.Name);
                        VariablesByFieldName.Remove(field.Name);
                        VariablesByFieldName.Add(field.Name, new VariableItem(field.Resolve(), new ParameterDefinition(field.Name, ParameterAttributes.Out, field.FieldType.MakeByReferenceType())));
                    }
                }
                // Next, look for direct field calls, pre-designed as in param, but can be overridden by the following logic
                foreach (Instruction? instruction in Method.Body.Instructions) {
                    if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && IsTargetCollisionField(field)) {

                        if (!VariablesByFieldName.TryGetValue(field.Name, out VariableItem _)) {
                            VariablesByFieldName.Add(field.Name, new VariableItem(field.Resolve(), new ParameterDefinition(field.Name, ParameterAttributes.None, field.FieldType)));
                        }
                    }
                }

                foreach (Instruction? instruction in Method.Body.Instructions) {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference calleeRef) {
                        var calleeId = calleeRef.GetIdentifier();
                        if (collection.TryGetValue(calleeId, out MethodWithPreparedVariables? calleeData)) {

                            CalleesById.TryAdd(calleeId, calleeData);

                            if (calleeData.VariablesByFieldName.Values.Any(v => v.Mode is VariableMode.Local && _preferredOutParamFieldNames.Contains(v.Field.Name))) {
                                Console.WriteLine($"【PrepareVariables】Promote preferred out-params ({string.Join(",", _preferredOutParamFieldNames)}) for callee {calleeData.Method.GetDebugName()} (called from {Method.GetDebugName()})");
                                calleeData.PrepareVariables(collection, [.. _preferredOutParamFieldNames]);
                            }

                            foreach (VariableItem? inheritedVariable in calleeData.VariablesByFieldName.Values.ToArray()) {
                                if (inheritedVariable.Mode is VariableMode.Local) {
                                    // Inherit the variable of the called function
                                    if (!VariablesByFieldName.TryGetValue(inheritedVariable.FieldName, out VariableItem? old)) {
                                        VariablesByFieldName.Add(inheritedVariable.FieldName,
                                            new VariableItem(
                                                inheritedVariable.Field,
                                                new VariableDefinition(inheritedVariable.Field.FieldType)));
                                    }
                                    else if (old.Mode is VariableMode.InParam) {
                                        VariablesByFieldName.Remove(inheritedVariable.FieldName);
                                        VariablesByFieldName.Add(inheritedVariable.FieldName,
                                            new VariableItem(
                                                inheritedVariable.Field,
                                                new VariableDefinition(inheritedVariable.Field.FieldType)));
                                    }
                                }
                                else if (inheritedVariable.Mode is VariableMode.OutParam) {
                                    if (VariablesByFieldName.TryGetValue(inheritedVariable.FieldName, out VariableItem? item)) {
                                        VariableMode expectedMode = VariableMode.Local;
                                        if (_preferredOutParamFieldNames.Contains(inheritedVariable.FieldName)) {
                                            expectedMode = VariableMode.OutParam;
                                        }
                                        if (item.Mode != expectedMode) {
                                            VariablesByFieldName.Remove(inheritedVariable.FieldName);
                                        }
                                    }
                                    if (!VariablesByFieldName.ContainsKey(inheritedVariable.FieldName)) {
                                        if (_preferredOutParamFieldNames.Contains(inheritedVariable.FieldName)) {
                                            VariablesByFieldName.Add(inheritedVariable.FieldName,
                                                new VariableItem(
                                                    inheritedVariable.Field,
                                                    new ParameterDefinition(inheritedVariable.FieldName, ParameterAttributes.Out, inheritedVariable.Field.FieldType.MakeByReferenceType())));
                                        }
                                        else {
                                            VariablesByFieldName.Add(inheritedVariable.FieldName,
                                                new VariableItem(
                                                    inheritedVariable.Field,
                                                    new VariableDefinition(inheritedVariable.Field.FieldType)));
                                        }
                                    }
                                }
                                else {
                                    if (!VariablesByFieldName.ContainsKey(inheritedVariable.FieldName)) {
                                        VariablesByFieldName.Add(inheritedVariable.FieldName,
                                            new VariableItem(
                                                inheritedVariable.Field,
                                                new ParameterDefinition(inheritedVariable.FieldName, ParameterAttributes.None, inheritedVariable.Field.FieldType)));
                                    }
                                }
                            }
                        }
                    }
                }

                var key = Method.GetIdentifier();
                if (collection.TryGetValue(key, out MethodWithPreparedVariables? m)) {
                    if (m != this) {
                        throw new InvalidOperationException();
                    }
                }
                else {
                    collection.Add(key, this);
                }
            }
        }
    }
}
