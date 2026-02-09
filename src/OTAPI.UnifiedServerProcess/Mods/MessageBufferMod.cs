using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.IO;
using System.Linq;

[Modification(ModType.PostMerge, "Add overload of GetData", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void PatchMessageBuffer(ModFwModder modder) {
    ModuleDefinition module = modder.Module;

    TypeReference byteRef = module.TypeSystem.Byte;
    var binaryReaderRef = new TypeReference("System.IO", "BinaryReader", module, module.TypeSystem.CoreLibrary);


    TypeDefinition messageBufferTypeDef = modder.Module.GetType("Terraria.MessageBuffer");
    MethodDefinition origMethod = messageBufferTypeDef.GetMethod("GetData");
    MethodDefinition overload = origMethod.Clone();
    var param_readbuffer = new ParameterDefinition("readbuffer", ParameterAttributes.None, byteRef.MakeArrayType());
    overload.Parameters.Add(param_readbuffer);
    var param_reader = new ParameterDefinition("reader", ParameterAttributes.None, binaryReaderRef);
    overload.Parameters.Add(param_reader);
    origMethod.DeclaringType.Methods.Add(overload);

    foreach (Instruction? inst in overload.Body.Instructions.ToArray()) {
        if (inst.Operand is FieldReference { Name: "readBuffer", DeclaringType.FullName: "Terraria.MessageBuffer" }) {
            Instruction loadThis = inst.Previous;
            if (!MonoModCommon.IL.TryGetReferencedParameter(overload, loadThis, out ParameterDefinition? paramThis) || paramThis.ParameterType.FullName != "Terraria.MessageBuffer") {
                throw new Exception("Failed to get paramThis");
            }
            Instruction loadReadBuffer = MonoModCommon.IL.BuildParameterLoad(overload, overload.Body, param_readbuffer);
            loadThis.OpCode = loadReadBuffer.OpCode;
            loadThis.Operand = loadReadBuffer.Operand;
            overload.Body.Instructions.Remove(inst);
        }
        else if (inst.Operand is FieldReference { Name: "reader", DeclaringType.FullName: "Terraria.MessageBuffer" }) {
            Instruction loadThis = inst.Previous;
            if (!MonoModCommon.IL.TryGetReferencedParameter(overload, loadThis, out ParameterDefinition? paramThis) || paramThis.ParameterType.FullName != "Terraria.MessageBuffer") {
                throw new Exception("Failed to get paramThis");
            }
            Instruction loadReadBuffer = MonoModCommon.IL.BuildParameterLoad(overload, overload.Body, param_reader);
            loadThis.OpCode = loadReadBuffer.OpCode;
            loadThis.Operand = loadReadBuffer.Operand;
            overload.Body.Instructions.Remove(inst);
        }
        else if (inst.Operand is MethodReference { Name: "ResetReader", DeclaringType.FullName: "Terraria.MessageBuffer" }) {

            Instruction loadThis = inst.Previous;
            if (!MonoModCommon.IL.TryGetReferencedParameter(overload, loadThis, out ParameterDefinition? paramThis) || paramThis.ParameterType.FullName != "Terraria.MessageBuffer") {
                throw new Exception("Failed to get paramThis");
            }

            var resetReaderRef = new MethodReference("ResetReader", module.TypeSystem.Void, messageBufferTypeDef) { HasThis = true };
            resetReaderRef.Parameters.AddRange([
                new(byteRef.MakeArrayType()),
                new(binaryReaderRef.MakeByReferenceType()),
            ]);
            inst.Operand = resetReaderRef;

            ILProcessor il = overload.Body.GetILProcessor();
            il.InsertAfter(loadThis, [
                MonoModCommon.IL.BuildParameterLoad(overload, overload.Body, param_readbuffer),
                MonoModCommon.IL.BuildParameterLoadAddress(overload, overload.Body, param_reader),
            ]);
        }
    }

    origMethod.Body.Variables.Clear();
    origMethod.Body.ExceptionHandlers.Clear();
    origMethod.Body.Instructions.Clear();
    Collection<Instruction> body = origMethod.Body.Instructions;

    body.Add(Instruction.Create(OpCodes.Ldarg_0));
    foreach (ParameterDefinition? p in origMethod.Parameters) {
        body.Add(MonoModCommon.IL.BuildParameterLoad(origMethod, origMethod.Body, p));
    }
    body.Add(Instruction.Create(OpCodes.Ldarg_0));
    body.Add(Instruction.Create(OpCodes.Ldfld, new FieldReference("readBuffer", param_readbuffer.ParameterType, messageBufferTypeDef)));
    body.Add(Instruction.Create(OpCodes.Ldarg_0));
    body.Add(Instruction.Create(OpCodes.Ldfld, new FieldReference("reader", param_reader.ParameterType, messageBufferTypeDef)));
    var overloadRef = new MethodReference(overload.Name, overload.ReturnType, overload.DeclaringType) { HasThis = overload.HasThis };
    overloadRef.Parameters.AddRange(overload.Parameters.Select(p => new ParameterDefinition(p.ParameterType)));
    body.Add(Instruction.Create(OpCodes.Call, overloadRef));
    body.Add(Instruction.Create(OpCodes.Ret));
}

namespace Terraria
{
    public class MessageBuffer
    {
        public void ResetReader(byte[] readBuffer, out BinaryReader reader) {
            reader = new BinaryReader(new MemoryStream(readBuffer));
        }
    }
}
