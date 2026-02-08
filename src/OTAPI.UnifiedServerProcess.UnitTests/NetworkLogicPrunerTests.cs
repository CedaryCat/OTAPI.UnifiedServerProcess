using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core;
using System.IO;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class NetworkLogicPrunerTests
    {
        [Fact]
        public void Prune_DoesNotBreakExceptionHandlers_AndNopsDeadBranch() {
            using var module = ModuleDefinition.CreateModule("USP.Pruner.Test", ModuleKind.Dll);

            var terrariaMain = new TypeDefinition(
                @namespace: "Terraria",
                name: "Main",
                attributes: TypeAttributes.Public | TypeAttributes.Class,
                baseType: module.TypeSystem.Object
            );
            var dedServ = new FieldDefinition(
                name: "dedServ",
                attributes: FieldAttributes.Public | FieldAttributes.Static,
                fieldType: module.TypeSystem.Boolean
            );
            terrariaMain.Fields.Add(dedServ);
            module.Types.Add(terrariaMain);

            var pruneTarget = new TypeDefinition(
                @namespace: "Test",
                name: "PruneTarget",
                attributes: TypeAttributes.Public | TypeAttributes.Class,
                baseType: module.TypeSystem.Object
            );
            module.Types.Add(pruneTarget);

            var method = new MethodDefinition(
                name: "M",
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                returnType: module.TypeSystem.Void
            );
            pruneTarget.Methods.Add(method);

            method.Body.InitLocals = false;

            var il = method.Body.GetILProcessor();

            var iTryStart = Instruction.Create(OpCodes.Ldsfld, dedServ);
            var iClientLabel = Instruction.Create(OpCodes.Nop);
            var iHandlerStart = Instruction.Create(OpCodes.Nop);
            var iAfterFinally = Instruction.Create(OpCodes.Ret);

            il.Append(iTryStart);
            il.Append(Instruction.Create(OpCodes.Brfalse_S, iClientLabel));
            il.Append(Instruction.Create(OpCodes.Ldstr, "server"));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Leave_S, iAfterFinally));

            il.Append(iClientLabel);
            il.Append(Instruction.Create(OpCodes.Ldstr, "client"));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Leave_S, iAfterFinally));

            il.Append(iHandlerStart);
            il.Append(Instruction.Create(OpCodes.Ldstr, "finally"));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Endfinally));
            il.Append(iAfterFinally);

            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
                TryStart = iTryStart,
                TryEnd = iHandlerStart,
                HandlerStart = iHandlerStart,
                HandlerEnd = iAfterFinally,
            });

            new NetworkLogicPruner(module).Prune();

            Assert.DoesNotContain(method.Body.Instructions, inst =>
                inst.Operand is FieldReference fr && fr.FullName == dedServ.FullName);

            Assert.DoesNotContain(method.Body.Instructions, inst =>
                inst.OpCode.Code == Code.Ldstr && (string)inst.Operand == "client");
            Assert.Single(method.Body.ExceptionHandlers);
            Assert.Contains(method.Body.Instructions, inst => inst.OpCode.Code == Code.Endfinally);
            Assert.Contains(method.Body.Instructions, inst => inst.OpCode.Code is Code.Leave or Code.Leave_S);

            using var ms = new MemoryStream();
            module.Write(ms);
            ms.Position = 0;

            using var reloaded = ModuleDefinition.ReadModule(ms);
            Assert.NotNull(reloaded);
        }

        [Fact]
        public void Prune_RemovesFullyUnreachableExceptionHandlers() {
            using var module = ModuleDefinition.CreateModule("USP.Pruner.Test", ModuleKind.Dll);

            var terrariaMain = new TypeDefinition(
                @namespace: "Terraria",
                name: "Main",
                attributes: TypeAttributes.Public | TypeAttributes.Class,
                baseType: module.TypeSystem.Object
            );
            var dedServ = new FieldDefinition(
                name: "dedServ",
                attributes: FieldAttributes.Public | FieldAttributes.Static,
                fieldType: module.TypeSystem.Boolean
            );
            terrariaMain.Fields.Add(dedServ);
            module.Types.Add(terrariaMain);

            var pruneTarget = new TypeDefinition(
                @namespace: "Test",
                name: "PruneTarget",
                attributes: TypeAttributes.Public | TypeAttributes.Class,
                baseType: module.TypeSystem.Object
            );
            module.Types.Add(pruneTarget);

            var method = new MethodDefinition(
                name: "M_UnreachableEH",
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                returnType: module.TypeSystem.Void
            );
            pruneTarget.Methods.Add(method);

            var il = method.Body.GetILProcessor();

            var iEntry = Instruction.Create(OpCodes.Ldsfld, dedServ);
            var iServerRet = Instruction.Create(OpCodes.Ret);
            var iTryStart = Instruction.Create(OpCodes.Ldstr, "client-try");
            var iHandlerStart = Instruction.Create(OpCodes.Pop);

            il.Append(iEntry);
            il.Append(Instruction.Create(OpCodes.Brtrue_S, iServerRet));
            il.Append(iTryStart);
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Leave_S, iServerRet));
            il.Append(iHandlerStart);
            il.Append(Instruction.Create(OpCodes.Leave_S, iServerRet));
            il.Append(iServerRet);

            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
                CatchType = module.ImportReference(typeof(System.Exception)),
                TryStart = iTryStart,
                TryEnd = iHandlerStart,
                HandlerStart = iHandlerStart,
                HandlerEnd = iServerRet,
            });

            Assert.Single(method.Body.ExceptionHandlers);

            new NetworkLogicPruner(module).Prune();

            Assert.Empty(method.Body.ExceptionHandlers);
            Assert.DoesNotContain(method.Body.Instructions, inst =>
                inst.Operand is FieldReference fr && fr.FullName == dedServ.FullName);

            using var ms = new MemoryStream();
            module.Write(ms);
            ms.Position = 0;

            using var reloaded = ModuleDefinition.ReadModule(ms);
            Assert.NotNull(reloaded);
        }
    }
}
