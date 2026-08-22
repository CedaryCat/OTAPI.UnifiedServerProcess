using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    internal static class AmbientDiagnosticsPatchCatalog
    {
        public const string RootContextTypeName = "UnifiedServerProcess.RootContext";
        public const string AmbientTypeName = "AmbientDiagnostics";

        public static readonly IReadOnlyDictionary<string, string> RedirectedSinks =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["System.Console::Write(System.String)"] = "Write",
                ["System.Console::WriteLine(System.String)"] = "WriteLine",
                ["Terraria.WorldGen::BroadcastText(Terraria.Localization.NetworkText,Microsoft.Xna.Framework.Color)"] = "BroadcastText",
            };

        public static readonly (string MethodIdentifier, int ExpectedRedirects)[] DiagnosticMethods = [
            ("Terraria.Testing.Invariant::Assert(System.Boolean,System.String)", 3),
            ("Terraria.Net.LegacyNetBufferPool::PrintBufferSizes()", 5),
            ("Terraria.DataStructures.BufferPool::PrintBufferSizes()", 5),
        ];

        public static TypeDefinition GetAmbientType(ModuleDefinition module) {
            TypeDefinition root = module.GetType(RootContextTypeName)
                ?? throw new InvalidOperationException($"Missing required type '{RootContextTypeName}'.");
            return root.NestedTypes.SingleOrDefault(type => type.Name == AmbientTypeName)
                ?? throw new InvalidOperationException(
                    $"Missing required type '{RootContextTypeName}/{AmbientTypeName}'.");
        }

        public static MethodDefinition GetMethod(
            TypeDefinition type,
            string name,
            params string[] parameterTypeNames) {

            return type.Methods.SingleOrDefault(method =>
                    method.Name == name
                    && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(parameterTypeNames, StringComparer.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Missing required method '{type.FullName}::{name}({string.Join(',', parameterTypeNames)})'.");
        }
    }

    /// <summary>
    /// Redirects explicitly approved diagnostic sinks before the call graph and context-bound
    /// field analysis are constructed. The approved methods are a semantic policy list rather
    /// than a module-wide Console rewrite.
    /// </summary>
    public sealed class AmbientDiagnosticsPrePatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(AmbientDiagnosticsPrePatcher);

        public override void Patch() {
            TypeDefinition ambient = AmbientDiagnosticsPatchCatalog.GetAmbientType(module);
            Dictionary<string, MethodDefinition> helperBySink = [];

            foreach (KeyValuePair<string, string> redirectedSink in AmbientDiagnosticsPatchCatalog.RedirectedSinks) {
                MethodDefinition helper = ambient.Methods.SingleOrDefault(method =>
                        method.Name == redirectedSink.Value
                        && method.GetIdentifier(withDeclaring: false)
                            == redirectedSink.Key[(redirectedSink.Key.IndexOf("::", StringComparison.Ordinal) + 2)..])
                    ?? throw new InvalidOperationException(
                        $"Ambient helper for '{redirectedSink.Key}' was not found.");
                helperBySink.Add(redirectedSink.Key, helper);
            }

            Dictionary<string, MethodDefinition> methods = module.GetAllTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .ToDictionary(method => method.GetIdentifier(), StringComparer.Ordinal);

            foreach ((string methodIdentifier, int expectedRedirects) in AmbientDiagnosticsPatchCatalog.DiagnosticMethods) {
                if (!methods.TryGetValue(methodIdentifier, out MethodDefinition? method)) {
                    throw new InvalidOperationException(
                        $"Approved ambient diagnostic method '{methodIdentifier}' was not found.");
                }

                int replacements = 0;
                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.OpCode.Code is not Code.Call and not Code.Callvirt
                        || instruction.Operand is not MethodReference called
                        || !helperBySink.TryGetValue(called.GetIdentifier(), out MethodDefinition? helper)) {
                        continue;
                    }

                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = helper;
                    replacements++;
                }

                if (replacements != expectedRedirects) {
                    throw new InvalidOperationException(
                        $"Approved ambient diagnostic method '{methodIdentifier}' redirected {replacements} sinks; " +
                        $"expected {expectedRedirects}. Update the semantic policy for this Terraria version.");
                }

                Info("Redirected {0} ambient diagnostic sinks in {1}.", replacements, methodIdentifier);
            }
        }
    }

    /// <summary>
    /// Finalizes ambient diagnostic helpers after RootContext SystemContext fields exist and
    /// places the ambient scope outside Main.Update's RuntimeDetour boundary.
    /// </summary>
    public sealed class AmbientDiagnosticsPostPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        private const string InvokeMainUpdateMethodName = "InvokeMainUpdate";

        public override string Name => nameof(AmbientDiagnosticsPostPatcher);

        public override void Patch() {
            TypeDefinition root = module.GetType(AmbientDiagnosticsPatchCatalog.RootContextTypeName)
                ?? throw new InvalidOperationException(
                    $"Missing required type '{AmbientDiagnosticsPatchCatalog.RootContextTypeName}'.");
            TypeDefinition ambient = AmbientDiagnosticsPatchCatalog.GetAmbientType(module);
            MethodDefinition tryGetCurrent = ambient.Methods.SingleOrDefault(method => method.Name == "TryGetCurrent")
                ?? throw new InvalidOperationException($"Missing '{ambient.FullName}::TryGetCurrent'.");

            FieldDefinition consoleField = root.Fields.SingleOrDefault(field => field.Name == "Console")
                ?? throw new InvalidOperationException($"Missing '{root.FullName}::Console'.");
            TypeDefinition consoleContext = consoleField.FieldType.Resolve()
                ?? throw new InvalidOperationException($"Could not resolve '{consoleField.FieldType.FullName}'.");
            MethodDefinition contextWrite = AmbientDiagnosticsPatchCatalog.GetMethod(
                consoleContext, "Write", module.TypeSystem.String.FullName);
            MethodDefinition contextWriteLine = AmbientDiagnosticsPatchCatalog.GetMethod(
                consoleContext, "WriteLine", module.TypeSystem.String.FullName);

            MethodReference systemWrite = module.ImportReference(
                typeof(Console).GetMethod(nameof(Console.Write), [typeof(string)])
                ?? throw new InvalidOperationException("Could not resolve System.Console.Write(string)."));
            MethodReference systemWriteLine = module.ImportReference(
                typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])
                ?? throw new InvalidOperationException("Could not resolve System.Console.WriteLine(string)."));

            MethodDefinition write = AmbientDiagnosticsPatchCatalog.GetMethod(
                ambient, "Write", module.TypeSystem.String.FullName);
            MethodDefinition writeLine = AmbientDiagnosticsPatchCatalog.GetMethod(
                ambient, "WriteLine", module.TypeSystem.String.FullName);

            ReplaceConsoleHelper(write, root, tryGetCurrent, consoleField, contextWrite, systemWrite);
            ReplaceConsoleHelper(writeLine, root, tryGetCurrent, consoleField, contextWriteLine, systemWriteLine);
            ReplaceBroadcastHelper(root, ambient, tryGetCurrent, systemWriteLine);
            WrapMainUpdate(root, ambient);
        }

        private static void ReplaceConsoleHelper(
            MethodDefinition helper,
            TypeDefinition root,
            MethodDefinition tryGetCurrent,
            FieldDefinition consoleField,
            MethodDefinition contextMethod,
            MethodReference fallbackMethod) {

            ResetBody(helper);
            var currentRoot = new VariableDefinition(root);
            helper.Body.Variables.Add(currentRoot);
            helper.Body.InitLocals = true;

            ILProcessor il = helper.Body.GetILProcessor();
            Instruction fallback = Instruction.Create(OpCodes.Ldarg_0);
            il.Append(Instruction.Create(OpCodes.Ldloca, currentRoot));
            il.Append(Instruction.Create(OpCodes.Call, tryGetCurrent));
            il.Append(Instruction.Create(OpCodes.Brfalse, fallback));
            il.Append(Instruction.Create(OpCodes.Ldloc, currentRoot));
            il.Append(Instruction.Create(OpCodes.Ldfld, consoleField));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Callvirt, contextMethod));
            il.Append(Instruction.Create(OpCodes.Ret));
            il.Append(fallback);
            il.Append(Instruction.Create(OpCodes.Call, fallbackMethod));
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        private void ReplaceBroadcastHelper(
            TypeDefinition root,
            TypeDefinition ambient,
            MethodDefinition tryGetCurrent,
            MethodReference fallbackWriteLine) {

            MethodDefinition helper = AmbientDiagnosticsPatchCatalog.GetMethod(
                ambient,
                "BroadcastText",
                "Terraria.Localization.NetworkText",
                "Microsoft.Xna.Framework.Color");
            FieldDefinition worldGenField = root.Fields.SingleOrDefault(field => field.Name == "WorldGen")
                ?? throw new InvalidOperationException($"Missing '{root.FullName}::WorldGen'.");
            TypeDefinition worldGenContext = worldGenField.FieldType.Resolve()
                ?? throw new InvalidOperationException($"Could not resolve '{worldGenField.FieldType.FullName}'.");
            MethodDefinition broadcastText = AmbientDiagnosticsPatchCatalog.GetMethod(
                worldGenContext,
                "BroadcastText",
                helper.Parameters[0].ParameterType.FullName,
                helper.Parameters[1].ParameterType.FullName);
            TypeDefinition networkText = helper.Parameters[0].ParameterType.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not resolve '{helper.Parameters[0].ParameterType.FullName}'.");
            MethodDefinition toString = AmbientDiagnosticsPatchCatalog.GetMethod(networkText, nameof(ToString));

            ResetBody(helper);
            var currentRoot = new VariableDefinition(root);
            helper.Body.Variables.Add(currentRoot);
            helper.Body.InitLocals = true;

            ILProcessor il = helper.Body.GetILProcessor();
            Instruction fallback = Instruction.Create(OpCodes.Ldarg_0);
            il.Append(Instruction.Create(OpCodes.Ldloca, currentRoot));
            il.Append(Instruction.Create(OpCodes.Call, tryGetCurrent));
            il.Append(Instruction.Create(OpCodes.Brfalse, fallback));
            il.Append(Instruction.Create(OpCodes.Ldloc, currentRoot));
            il.Append(Instruction.Create(OpCodes.Ldfld, worldGenField));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Call, broadcastText));
            il.Append(Instruction.Create(OpCodes.Ret));
            il.Append(fallback);
            il.Append(Instruction.Create(OpCodes.Callvirt, toString));
            il.Append(Instruction.Create(OpCodes.Call, fallbackWriteLine));
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        private void WrapMainUpdate(TypeDefinition root, TypeDefinition ambient) {
            TypeDefinition main = module.GetType("Terraria.Main")
                ?? throw new InvalidOperationException("Missing required type 'Terraria.Main'.");
            MethodDefinition dedServ = main.Methods.SingleOrDefault(method =>
                    method.Name == "mfwh_DedServ"
                    && method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.FullName == root.FullName)
                ?? throw new InvalidOperationException(
                    $"Missing 'Terraria.Main::mfwh_DedServ({root.FullName})'.");

            Instruction[] updateCalls = dedServ.Body.Instructions.Where(instruction =>
                    instruction.OpCode.Code is Code.Call or Code.Callvirt
                    && instruction.Operand is MethodReference called
                    && called.Name == "Update"
                    && called.DeclaringType.FullName == "Terraria.Server.Game"
                    && called.Parameters.Count == 2
                    && called.Parameters[0].ParameterType.FullName == root.FullName
                    && called.Parameters[1].ParameterType.FullName == "Microsoft.Xna.Framework.GameTime")
                .ToArray();
            if (updateCalls.Length != 1) {
                throw new InvalidOperationException(
                    $"Expected exactly one context-bound Terraria.Server.Game.Update call in '{dedServ.FullName}', " +
                    $"but found {updateCalls.Length}.");
            }

            var updateReference = (MethodReference)updateCalls[0].Operand;
            MethodDefinition wrapper = CreateUpdateWrapper(root, ambient, updateReference);
            updateCalls[0].OpCode = OpCodes.Call;
            updateCalls[0].Operand = wrapper;
            Info("Wrapped {0} outside its RuntimeDetour boundary with {1}.", updateReference.FullName, wrapper.FullName);
        }

        private MethodDefinition CreateUpdateWrapper(
            TypeDefinition root,
            TypeDefinition ambient,
            MethodReference updateReference) {

            if (ambient.Methods.Any(method => method.Name == InvokeMainUpdateMethodName)) {
                throw new InvalidOperationException(
                    $"'{ambient.FullName}::{InvokeMainUpdateMethodName}' already exists.");
            }

            MethodDefinition push = AmbientDiagnosticsPatchCatalog.GetMethod(ambient, "Push", root.FullName);
            MethodDefinition pop = AmbientDiagnosticsPatchCatalog.GetMethod(ambient, "Pop", root.FullName);
            var wrapper = new MethodDefinition(
                InvokeMainUpdateMethodName,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            wrapper.Parameters.Add(new ParameterDefinition("game", ParameterAttributes.None, updateReference.DeclaringType));
            wrapper.Parameters.Add(new ParameterDefinition("root", ParameterAttributes.None, root));
            wrapper.Parameters.Add(new ParameterDefinition(
                "gameTime",
                ParameterAttributes.None,
                updateReference.Parameters[1].ParameterType));
            ambient.Methods.Add(wrapper);

            var previous = new VariableDefinition(root);
            wrapper.Body.Variables.Add(previous);
            wrapper.Body.InitLocals = true;
            ILProcessor il = wrapper.Body.GetILProcessor();

            Instruction tryStart = Instruction.Create(OpCodes.Ldarg_0);
            Instruction finallyStart = Instruction.Create(OpCodes.Ldloc, previous);
            Instruction methodEnd = Instruction.Create(OpCodes.Ret);

            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Call, push));
            il.Append(Instruction.Create(OpCodes.Stloc, previous));
            il.Append(tryStart);
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Callvirt, updateReference));
            il.Append(Instruction.Create(OpCodes.Leave, methodEnd));
            il.Append(finallyStart);
            il.Append(Instruction.Create(OpCodes.Call, pop));
            il.Append(Instruction.Create(OpCodes.Endfinally));
            il.Append(methodEnd);

            wrapper.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
                TryStart = tryStart,
                TryEnd = finallyStart,
                HandlerStart = finallyStart,
                HandlerEnd = methodEnd,
            });
            return wrapper;
        }

        private static void ResetBody(MethodDefinition method) {
            if (!method.HasBody) {
                method.Body = new MethodBody(method);
                return;
            }
            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            method.Body.InitLocals = false;
        }
    }
}
