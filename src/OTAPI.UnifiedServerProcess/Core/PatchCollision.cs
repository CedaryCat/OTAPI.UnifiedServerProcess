using Microsoft.Xna.Framework;
using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace OTAPI.UnifiedServerProcess.Core
{
    public partial class PatchLogic
    {
        public static void PatchCollision(ModuleDefinition module) {

            // Dictionary to store lookUp Mutations
            Dictionary<string, MethodWithPreparedVariables> methodsWithVariables = new();

            // Get the type definition for Terraria.Collision
            var collisionType = module.GetType("Terraria.Collision");

            // Iterate through each lookUp in the collision type
            foreach (var method in collisionType.Methods.Where(m => m.Name != ".cctor" && m.Name != ".ctor")) {
                // Iterate through each instruction in the lookUp
                var arr = method.Body.Instructions.ToArray();
                foreach (var instruction in arr) {
                    if (instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference setField && setField.DeclaringType.FullName == "Terraria.Collision") {
                        if (!methodsWithVariables.TryGetValue(method.GetIdentifier(), out var methodData)) {
                            methodData = new MethodWithPreparedVariables(method);
                            methodData.PrepareVariables(methodsWithVariables);
                            break;
                        }
                    }
                }
                foreach (var instruction in arr) {
                    if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference LdsOperand && LdsOperand.Name == nameof(Collision._cacheForConveyorBelts)) {
                        instruction.OpCode = OpCodes.Newobj;
                        instruction.Operand = new MethodReference(".ctor", module.TypeSystem.Void, LdsOperand.FieldType) { HasThis = true };
                    }
                }
            }

            Console.WriteLine($"Found {methodsWithVariables.Count} methods that assign to Terraria.Collision static variables:");
            foreach (var m in methodsWithVariables.Values) {
                Console.WriteLine($"    【{m.Method.GetDebugName()}】 | {string.Join(",", m.VariableItems.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();


            Dictionary<string, MethodWithPreparedVariables> methodsWithVariables_read = new();

            foreach (var type in module.Types) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }

                    var ilProcessor = method.Body.GetILProcessor();

                    var arr = method.Body.Instructions.ToArray();
                    foreach (var instruction in arr) {
                        if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference getField &&
                            getField.DeclaringType.FullName == "Terraria.Collision" && getField.Name != nameof(Collision.Epsilon)) {

                            if (!methodsWithVariables.TryGetValue(method.GetIdentifier(), out var added)) {
                                added = new MethodWithPreparedVariables(method);
                                added.PrepareVariables(methodsWithVariables);
                            }
                            methodsWithVariables_read.TryAdd(method.GetIdentifier(), added);

                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Found {methodsWithVariables_read.Count} methods that read from Terraria.Collision static variables:");
            foreach (var kv in methodsWithVariables_read) {
                var m = kv.Value;
                Console.WriteLine($"    【{m.Method.GetDebugName()}】 | {string.Join(",", m.VariableItems.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            Dictionary<string, MethodWithPreparedVariables> methodsWithVariablesLastDeepth = new(methodsWithVariables);
            Dictionary<string, MethodWithPreparedVariables> methodsWithVariablesThisDeepth = new();


            for (int currentDeepth = 0; currentDeepth < 6; currentDeepth++) {
                // Iterate through each type in the module
                foreach (var type in module.Types) {
                    // Iterate through each lookUp in the type
                    foreach (var method in type.Methods) {
                        if (!method.HasBody) {
                            continue;
                        }

                        bool anyCall = false;

                        // Iterate through each instruction in the lookUp
                        var arr = method.Body.Instructions.ToArray();
                        foreach (var instruction in arr) {
                            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {

                                if (methodsWithVariablesLastDeepth.TryGetValue(methodRef.GetIdentifier(), out var methodData)) {
                                    Console.WriteLine($"In the {currentDeepth + 2}th level of call, {method.Name} called the method {methodRef.Name} in the last level, recorded");
                                    anyCall = true;
                                }
                            }
                        }

                        if (anyCall) {
                            if (!methodsWithVariables.TryGetValue(method.GetIdentifier(), out var added)) {
                                added = new MethodWithPreparedVariables(method);
                                added.PrepareVariables(methodsWithVariables);
                            }
                            else {
                                added.PrepareVariables(methodsWithVariables);
                            }
                            methodsWithVariablesThisDeepth.TryAdd(method.GetIdentifier(), added);
                        }
                    }
                }

                Console.WriteLine($"In the {currentDeepth + 2}th level of call, found {methodsWithVariablesThisDeepth.Count} methods: 【Method Name】| [Operation Parameters]");
                foreach (var m in methodsWithVariablesThisDeepth.Values) {
                    Console.WriteLine($"    【{m.Method.GetDebugName()}】| {string.Join(",", m.VariableItems.Values)}");
                }
                //Console.WriteLine($"Press Enter to continue");
                //Console.ReadLine();

                methodsWithVariablesLastDeepth = new(methodsWithVariablesThisDeepth);
                methodsWithVariablesThisDeepth.Clear();
            }

            Dictionary<string, MethodWithPreparedVariables> requiredPushValue = methodsWithVariables.Values.Where(m => m.VariableItems.Values.Any(v => v.mode == VariableMode.InParam)).ToDictionary(x => x.GetIdentifier, x => x);

            foreach (var m in methodsWithVariables.Values.ToArray()) {
                HashSet<string> inputs = new();
                var reversed = m.Method.Body.Instructions.Reverse().ToArray();
                foreach (var instruction in reversed) {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {
                        if (requiredPushValue.TryGetValue(methodRef.GetIdentifier(), out var methodCallingWithInPut)) {
                            foreach (var item in methodCallingWithInPut.VariableItems.Values.Where(
                                v => v.mode == VariableMode.InParam && v.Name is not nameof(Collision.shimmer) or nameof(Collision.honey))) {
                                inputs.Add(item.Name);
                            }
                            Console.WriteLine($"Detected that {m.Method.GetDebugName()} calls the method {methodRef.GetDebugName()} that requires external parameters: {string.Join(",", inputs)}");
                            Console.WriteLine($"    Set the upstream parameter before {methodRef.GetDebugName()} in the body of {m.Method.GetDebugName()} to OutParam mode");
                        }
                        if (methodsWithVariables.TryGetValue(methodRef.GetIdentifier(), out var methodCalling)) {
                            var canTransferOutParams = methodCalling.VariableItems.Values.Where(v => inputs.Contains(v.Name) && v.mode != VariableMode.InParam).ToArray();

                            methodCalling.PrepareVariables(methodsWithVariables, [.. canTransferOutParams.Select(v => v.fieldDefinition.Name)]);
                            foreach (var item in canTransferOutParams) {
                                inputs.Remove(item.Name);
                                Console.WriteLine($"    Set the upstream parameter before {methodRef.GetDebugName()} in the body of {m.Method.GetDebugName()} to OutParam mode");
                                Console.WriteLine($"    The remaining input parameters are: {string.Join(",", inputs)}");
                            }
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Ldsfld &&
                        instruction.Operand is FieldReference field &&
                        field.DeclaringType.FullName == "Terraria.Collision" &&
                        field.Name != nameof(Collision.Epsilon)) {

                        inputs.Add(field.Name);
                        Console.WriteLine($"Detected that {m.Method.GetDebugName()} calls the field {field.Name}");
                        Console.WriteLine($"    The upstream parameter before {field.Name} in the body of {m.Method.GetDebugName()} is set to OutParam mode");
                    }
                }
            }

            HashSet<string> predefined = [
                MonoModCommon.Reference.Method(() => Collision.SlopeCollision(default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.noSlopeCollision(default,default,default,default,default,default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.TileCollision(default, default, default, default, default, default, default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => Collision.AdvancedTileCollision(default, default, default, default, default, default, default, default)).GetSimpleIdentifier(),
                MonoModCommon.Reference.Method(() => default(Player)!.SlopingCollision(default,default)).GetSimpleIdentifier(),
            ];

            foreach (var m in methodsWithVariables.Values.ToArray()) {
                if (predefined.Contains(m.GetIdentifier)) {
                    m.PrepareVariables(methodsWithVariables, [.. m.VariableItems.Values.Where(v => v.mode is not VariableMode.InParam).Select(v => v.fieldDefinition.Name)]);
                }
                else {
                    m.PrepareVariables(methodsWithVariables);
                }
            }

            foreach (var m in methodsWithVariables.Values.ToArray()) {
                m.PrepareVariables(methodsWithVariables);
            }

            List<MethodWithPreparedVariables> lookUps = [.. methodsWithVariables.Values.Where(m => m.VariableItems.Values.Any(v => v.mode != VariableMode.Local))];
            Dictionary<string, MethodWithPreparedVariables> involvedMethods = new(lookUps.ToDictionary(l => l.GetIdentifier, l => l));


            Console.WriteLine($"Finished, there are {methodsWithVariables.Count} related methods, and {lookUps.Count} methods have been filtered out:");
            foreach (var m in lookUps) {
                Console.WriteLine($"    【 {m.Method.GetDebugName()} 】 | {string.Join(",", m.VariableItems.Values)}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            //Avoid infinite recursion
            HashSet<string> allRemoved = [];
            int round = 1;
            while (lookUps.Count > 0) {
                round++;
                Dictionary<string, MethodWithPreparedVariables> removed = new();
                var arr = lookUps.ToArray();
                foreach (var lookUp in arr) {
                    foreach (var kv in methodsWithVariables) {
                        var method = kv.Value;
                        var key = kv.Key;

                        if (method.MyCallingMethods.ContainsKey(lookUp.GetIdentifier)) {
                            Console.WriteLine($"Found that {method.Method.GetDebugName()} calls a non-local variable function {lookUp.Method.GetDebugName()}, added to the necessary function list");

                            involvedMethods.TryAdd(kv.Key, method);

                            if (!allRemoved.Contains(method.GetIdentifier) && method.VariableItems.Values.Any(v => v.mode != VariableMode.Local)) {
                                Console.WriteLine($"Detected that {method.Method.GetDebugName()} is a non-local variable method, added to the task queue");
                                lookUps.Add(method);
                            }

                            removed.TryAdd(lookUp.GetIdentifier, lookUp);
                            allRemoved.Add(lookUp.GetIdentifier);
                            lookUps.Remove(lookUp);
                        }
                    }
                }

                Console.WriteLine($"This round processed non-local variable methods: {removed.Count} pieces, remaining {lookUps.Count} pieces");
                foreach (var m in removed.Values) {
                    Console.WriteLine($"    【 {m.Method.GetDebugName()} 】 | {string.Join(",", m.VariableItems.Values)}");
                }

                if (lookUps.Count > 0) {
                    //Console.WriteLine($"Press Enter to continue");
                    //Console.ReadLine();
                }
            }


            Console.WriteLine($"Found {involvedMethods.Count} methods:");
            foreach (var n in involvedMethods.Values) {
                n.InstallVariable();
                Console.WriteLine($"    【 {n.Method.GetDebugName()} 】 | {string.Join(",", n.VariableItems.Values)}");
                Console.WriteLine($"        【params】{string.Join(",", n.Method.Parameters.Where(p => n.VariableItems.Values.Any(nv => nv.IsSameVariableReference(p))).Select(v => v.Index))}");
                Console.WriteLine($"        【locals】{string.Join(",", n.Method.Body.Variables.Where(v => n.VariableItems.Values.Any(nv => nv.IsSameVariableReference(v))).Select(v => v.Index))}");
            }
            //Console.WriteLine($"Press Enter to continue");
            //Console.ReadLine();

            Console.WriteLine($"For the involved methods, modify the method body");
            foreach (var currentEditMethod in involvedMethods.Values) {

                if (currentEditMethod.VariableItems.Values.Any(v => v.mode is not VariableMode.Local)) {
                    foreach (var param in currentEditMethod.Method.Parameters) {
                        param.IsOptional = false;
                    }
                }

                var ilProcessor = currentEditMethod.Method.Body.GetILProcessor();

                foreach (var outParam in currentEditMethod.VariableItems.Values.Where(v => v.mode == VariableMode.OutParam)) {
                    var top = currentEditMethod.Method.Body.Instructions.First();

                    foreach (var instruction in outParam.SetValueFromStackInstrustion(currentEditMethod.Method, [ilProcessor.Create(OpCodes.Ldc_I4_0)])) {
                        ilProcessor.InsertBefore(top, instruction);
                    }
                }

                var cachedInstructions = currentEditMethod.Method.Body.Instructions.ToArray();
                foreach (var instruction in cachedInstructions) {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference methodRef) {

                        List<VariableItem> added = new();

                        var callingMethodName = methodRef.GetIdentifier();
                        if (involvedMethods.TryGetValue(callingMethodName, out var callingMethod)) {
                            foreach (var variable in callingMethod.VariableItems.Values) {

                                if (currentEditMethod.VariableItems.TryGetValue(variable.Name, out var item)) {
                                    if (variable.mode == VariableMode.InParam) {
                                        added.Add(item);
                                        foreach (var inst in item.PushValueToStackInstruction(currentEditMethod.Method)) {
                                            ilProcessor.InsertBefore(instruction, inst);
                                        }
                                    }
                                    else if (variable.mode == VariableMode.OutParam) {
                                        added.Add(item);
                                        foreach (var inst in item.PushAddressToStackInstruction(currentEditMethod.Method)) {
                                            ilProcessor.InsertBefore(instruction, inst);
                                        }
                                    }
                                    else {
                                        Console.WriteLine($"    【{currentEditMethod.Method.GetDebugName()}】Skip {methodRef.GetDebugName()} related variable：{variable}");
                                    }
                                }
                                else {
                                    Console.WriteLine($"    【{currentEditMethod.Method.GetDebugName()}】There is no {methodRef.GetDebugName()} related variable：{variable}");
                                }
                            }
                        }

                        if (added.Count > 0) {
                            Console.WriteLine($"    【{currentEditMethod.Method.GetDebugName()}】The call of {methodRef.GetDebugName()} is modified, add parameters：{string.Join(",", added)}");
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Ldsfld &&
                        instruction.Operand is FieldReference readField &&
                        readField.DeclaringType.FullName == "Terraria.Collision" &&
                        readField.Name != nameof(Collision.Epsilon)) {

                        if (currentEditMethod.VariableItems.TryGetValue(readField.Name, out var item)) {
                            var arr = item.PushValueToStackInstruction(currentEditMethod.Method);

                            foreach (var i in cachedInstructions) {
                                if (i.Operand == instruction) {
                                    i.Operand = arr[0];
                                }
                                if (i.Operand is ILLabel label && label.Target == instruction) {
                                    label.Target = arr[0];
                                }
                            }

                            Instruction[] rmInstructions = [instruction];
                            int[] rmIndex = [.. rmInstructions.Select(ilProcessor.Body.Instructions.IndexOf)];

                            ilProcessor.InsertAfter(instruction, arr.AsEnumerable());
                            foreach (var index in rmIndex) {
                                ilProcessor.Body.Instructions.RemoveAt(index);
                            }

                            Console.WriteLine($"    【{currentEditMethod.Method.GetDebugName()}】The call of Collision field [{readField.Name}] is modified, through the variable {item}");
                        }
                    }
                    else if (instruction.OpCode == OpCodes.Stsfld &&
                        instruction.Operand is FieldReference writeField &&
                        writeField.DeclaringType.FullName == "Terraria.Collision") {

                        if (currentEditMethod.VariableItems.TryGetValue(writeField.Name, out var item) && item.mode == VariableMode.OutParam) {
                            var arr = item.SetValueFromStackInstrustion(currentEditMethod.Method, [instruction.Previous]);

                            foreach (var i in cachedInstructions) {
                                if (i.Operand == instruction.Previous) {
                                    i.Operand = arr[0];
                                }
                                if (i.Operand is ILLabel label && label.Target == instruction.Previous) {
                                    label.Target = arr[0];
                                }
                            }

                            Instruction[] rmInstructions = [instruction.Previous, instruction];
                            int[] rmIndex = [.. rmInstructions.Select(ilProcessor.Body.Instructions.IndexOf)];

                            ilProcessor.InsertAfter(instruction, arr.AsEnumerable());
                            // Must use reverse order, otherwise the index of the front will be removed first, resulting in the index of the back - 1
                            foreach (var index in rmIndex.OrderByDescending(x => x)) {
                                ilProcessor.Body.Instructions.RemoveAt(index);
                            }

                            Console.WriteLine($"    【{currentEditMethod.Method.GetDebugName()}】The assignment of Collision field [{writeField.Name}] is modified, through the variable {item}");
                        }
                    }
                }

                //currentEditMethod.GetMethod.Parameters.Clear();
                //currentEditMethod.GetMethod.Parameters.AddRange(currentEditMethod.GetMethod.Parameters);
                //currentEditMethod.GetMethod.Body.Variables.Clear();
                //currentEditMethod.GetMethod.Body.Variables.AddRange(currentEditMethod.GetMethod.Body.Variables);
                //currentEditMethod.GetMethod.Body.Instructions.Clear();
                //currentEditMethod.GetMethod.Body.Instructions.AddRange(currentEditMethod.GetMethod.Body.Instructions);

                Console.WriteLine($"【{currentEditMethod.Method.GetDebugName()}】Modification completed");
            }

            Console.WriteLine($"Replacing all modified method calls");
            int count = 0;
            foreach (var type in module.Types) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (var instruction in method.Body.Instructions) {
                        if (instruction.Operand is MethodReference methodRef && involvedMethods.TryGetValue(methodRef.GetIdentifier(), out var newMethod)) {
                            instruction.Operand = MonoModCommon.Structure.DeepMapMethodReference(newMethod.Method, new());
                            count++;
                        }
                    }
                }
            }
            Console.WriteLine($"Replaced {count} method calls");

            foreach (var m in involvedMethods.Values) {
                if (!m.Method.DeclaringType.Methods.Any(other => other.Name == "mfwh_" + m.Method.Name)) {
                    continue;
                }

                foreach (var inst in m.Method.Body.Instructions) {
                    if (inst.Operand is not MethodReference { Name: ".ctor" } deleCtor) {
                        continue;
                    }
                    var delegateDef = deleCtor.DeclaringType.Resolve();
                    if (!delegateDef.IsDelegate()) {
                        continue;
                    }
                    var invokeDef = delegateDef.GetMethod("Invoke");
                    var beginInvoke = delegateDef.GetMethod("BeginInvoke");
                    invokeDef.Parameters.Clear();
                    var asyncCallback = beginInvoke.Parameters[^2];
                    var asyncState = beginInvoke.Parameters[^1];
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
    }

    [MonoMod.MonoModIgnore]
    public class VariableItem
    {
        public override string ToString() {
            return
                mode switch {
                    VariableMode.Local => $"[local {Name}]",
                    VariableMode.OutParam => $"[out {Name}]",
                    VariableMode.InParam => $"[in {Name}]",
                    _ => throw new NotImplementedException(),
                };
        }
        public readonly string Name;
        public readonly FieldDefinition fieldDefinition;
        public readonly VariableDefinition? variableDefinition;
        public readonly ParameterDefinition? parameterDefinition;
        public readonly VariableMode mode;
        public int GetIndex(MethodDefinition definition) {
            if (variableDefinition is not null) {
                return definition.Body.Variables.IndexOf(variableDefinition);
            }
            else if (parameterDefinition is not null) {
                return (definition.HasThis ? 1 : 0) + definition.Parameters.IndexOf(parameterDefinition);
            }
            else {
                throw new NotImplementedException();
            }
        }
        public VariableItem(FieldDefinition field, VariableDefinition variable) {
            Name = field.Name;
            fieldDefinition = field;
            variableDefinition = variable;
            mode = VariableMode.Local;
        }
        public VariableItem(FieldDefinition field, ParameterDefinition parameter) {
            Name = field.Name;
            fieldDefinition = field;
            parameterDefinition = parameter;
            if (parameter.IsOut) {
                mode = VariableMode.OutParam;
            }
            else {
                mode = VariableMode.InParam;
            }
        }
        public bool IsSameVariableReference(ParameterDefinition parameter) {
            if (parameterDefinition is null) {
                return false;
            }
            return parameter == parameterDefinition;
        }
        public bool IsSameVariableReference(VariableDefinition variable) {
            if (variableDefinition is null) {
                return false;
            }
            return variable == variableDefinition;
        }

        public void InstallMethodVariables(MethodDefinition definition) {
            if (variableDefinition is not null) {
                if (!definition.Body.Variables.Contains(variableDefinition)) {
                    definition.Body.Variables.Add(variableDefinition);
                }
            }
            else if (parameterDefinition is not null) {
                if (!definition.Parameters.Contains(parameterDefinition)) {
                    definition.Parameters.Add(parameterDefinition);
                }
            }
        }
        public Instruction[] PushValueToStackInstruction(MethodDefinition definition) {

            var index = GetIndex(definition);
            if (variableDefinition is not null) {
                var inst = Instruction.Create(OpCodes.Ldloc, variableDefinition);
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
            else if (parameterDefinition is not null) {
                var inst = Instruction.Create(OpCodes.Ldarg, parameterDefinition);
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
                if (mode == VariableMode.OutParam) {
                    OpCode opCode;
                    if (fieldDefinition.FieldType.Name == typeof(int).Name) {
                        opCode = OpCodes.Ldind_I4;
                    }
                    else if (fieldDefinition.FieldType.Name == typeof(bool).Name) {
                        opCode = OpCodes.Ldind_U1;
                    }
                    else {
                        throw new NotImplementedException($"Unknown type {fieldDefinition.FieldType.Name}");
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
        public Instruction[] PushAddressToStackInstruction(MethodDefinition definition) {
            var index = GetIndex(definition);
            if (variableDefinition is not null) {
                var inst = Instruction.Create(OpCodes.Ldloca, variableDefinition);
                if (index <= byte.MaxValue) {
                    inst.OpCode = OpCodes.Ldloca_S;
                }
                return [inst];
            }
            else if (parameterDefinition is not null) {
                if (mode == VariableMode.OutParam) {
                    var inst = Instruction.Create(OpCodes.Ldarg, parameterDefinition);
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
                    var inst = Instruction.Create(OpCodes.Ldarga, parameterDefinition);
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
        public Instruction[] SetValueFromStackInstrustion(MethodDefinition definition, Instruction[] valueInStack) {
            var index = GetIndex(definition);
            if (variableDefinition is not null) {
                var inst = Instruction.Create(OpCodes.Stloc, variableDefinition);
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
                return [.. valueInStack, inst];
            }
            else if (parameterDefinition is not null) {
                if (mode == VariableMode.OutParam) {
                    var inst = Instruction.Create(OpCodes.Ldarg, parameterDefinition);
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
                    if (fieldDefinition.FieldType.Name == typeof(int).Name) {
                        opCode = OpCodes.Stind_I4;
                    }
                    else if (fieldDefinition.FieldType.Name == typeof(bool).Name) {
                        opCode = OpCodes.Stind_I1;
                    }
                    else {
                        throw new NotImplementedException($"Unknown type {fieldDefinition.FieldType.Name}");
                    }
                    var setValueToAddress = Instruction.Create(opCode);
                    return [inst, .. valueInStack, setValueToAddress];
                }
                else {
                    var inst = Instruction.Create(OpCodes.Starg, parameterDefinition);
                    if (index <= byte.MaxValue) {
                        inst.OpCode = OpCodes.Starg_S;
                    }
                    return [.. valueInStack, inst];
                }
            }
            else {
                throw new InvalidOperationException();
            }
        }
    }
    [MonoMod.MonoModIgnore]
    public enum VariableMode
    {
        InParam,
        Local,
        OutParam,
    }
    [MonoMod.MonoModIgnore]
    public class MethodWithPreparedVariables
    {
        public readonly string GetIdentifier;
        public readonly MethodDefinition Method;
        public readonly Dictionary<string, VariableItem> VariableItems = new();
        public readonly Dictionary<string, MethodWithPreparedVariables> MyCallingMethods = new();
        private HashSet<string> PreferredOutParam = [];

        public MethodWithPreparedVariables(MethodDefinition method) {
            GetIdentifier = method.GetIdentifier();
            Method = method;
        }
        public void InstallVariable() {
            foreach (var variable in VariableItems.Values) {
                variable.InstallMethodVariables(Method);
            }
        }
        public void PrepareVariables(Dictionary<string, MethodWithPreparedVariables> collection, params string[] shouldChangeToOutParam) {
            foreach (var item in shouldChangeToOutParam) {
                PreferredOutParam.Add(item);
            }

            // First, look for direct field writes, this priority is highest
            foreach (var instruction in Method.Body.Instructions) {
                if (instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Terraria.Collision") {
                    PreferredOutParam.Add(field.Name);
                    VariableItems.Remove(field.Name);
                    VariableItems.Add(field.Name, new VariableItem(field.Resolve(), new ParameterDefinition(field.Name, ParameterAttributes.Out, field.FieldType.MakeByReferenceType())));
                }
            }
            // Next, look for direct field calls, pre-designed as in param, but can be overridden by the following logic
            foreach (var instruction in Method.Body.Instructions) {
                if (instruction.OpCode == OpCodes.Ldsfld &&
                    instruction.Operand is FieldReference field &&
                    field.DeclaringType.FullName == "Terraria.Collision" &&
                    field.Name != nameof(Collision.Epsilon)) {

                    if (!VariableItems.TryGetValue(field.Name, out var item)) {
                        VariableItems.Add(field.Name, new VariableItem(field.Resolve(), new ParameterDefinition(field.Name, ParameterAttributes.None, field.FieldType)));
                    }
                }
            }

            foreach (var instruction in Method.Body.Instructions) {
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) && instruction.Operand is MethodReference calleeRef) {
                    var calleeId = calleeRef.GetIdentifier();
                    if (collection.TryGetValue(calleeId, out var calleeData)) {

                        MyCallingMethods.TryAdd(calleeId, calleeData);

                        if (calleeData.VariableItems.Values.Any(v => v.mode is VariableMode.Local && PreferredOutParam.Contains(v.fieldDefinition.Name))) {
                            Console.WriteLine($"【Process PrepareVariables 1】The input parameter {string.Join(",", PreferredOutParam)} of callee {calleeData.Method.GetDebugName()} in caller {Method.GetDebugName()} is set to Out parameter");
                            calleeData.PrepareVariables(collection, [.. PreferredOutParam]);
                        }

                        foreach (var inhereVariable in calleeData.VariableItems.Values.ToArray()) {
                            if (inhereVariable.mode is VariableMode.Local) {
                                // Inherit the variable of the called function
                                if (!VariableItems.TryGetValue(inhereVariable.Name, out var old)) {
                                    VariableItems.Add(inhereVariable.Name,
                                        new VariableItem(
                                            inhereVariable.fieldDefinition,
                                            new VariableDefinition(inhereVariable.fieldDefinition.FieldType)));
                                }
                                else if (old.mode is VariableMode.InParam) {
                                    VariableItems.Remove(inhereVariable.Name);
                                    VariableItems.Add(inhereVariable.Name,
                                        new VariableItem(
                                            inhereVariable.fieldDefinition,
                                            new VariableDefinition(inhereVariable.fieldDefinition.FieldType)));
                                }
                            }
                            else if (inhereVariable.mode is VariableMode.OutParam) {
                                if (VariableItems.TryGetValue(inhereVariable.Name, out var item)) {
                                    var mode = VariableMode.Local;
                                    if (PreferredOutParam.Contains(inhereVariable.Name)) {
                                        mode = VariableMode.OutParam;
                                    }
                                    if (item.mode != mode) {
                                        VariableItems.Remove(inhereVariable.Name);
                                    } 
                                }
                                if (!VariableItems.ContainsKey(inhereVariable.Name)) {
                                    if (PreferredOutParam.Contains(inhereVariable.Name)) {
                                        VariableItems.Add(inhereVariable.Name, 
                                            new VariableItem(
                                                inhereVariable.fieldDefinition, 
                                                new ParameterDefinition(inhereVariable.Name, ParameterAttributes.Out, inhereVariable.fieldDefinition.FieldType.MakeByReferenceType())));
                                    }
                                    else {
                                        VariableItems.Add(inhereVariable.Name, 
                                            new VariableItem(
                                                inhereVariable.fieldDefinition,
                                                new VariableDefinition(inhereVariable.fieldDefinition.FieldType)));
                                    }
                                }
                            }
                            else {
                                if (!VariableItems.ContainsKey(inhereVariable.Name)) {
                                    VariableItems.Add(inhereVariable.Name, 
                                        new VariableItem(
                                            inhereVariable.fieldDefinition, 
                                            new ParameterDefinition(inhereVariable.Name, ParameterAttributes.None, inhereVariable.fieldDefinition.FieldType)));
                                }
                            }
                        }
                    }
                }
            }

            var key = Method.GetIdentifier();
            if (collection.TryGetValue(key, out var m)) {
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
