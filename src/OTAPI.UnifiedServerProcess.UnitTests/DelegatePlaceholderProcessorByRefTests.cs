using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.IO;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class DelegatePlaceholderProcessorByRefTests
    {
        [Fact]
        public void Apply_PropagatesDelegateRewriteThroughByRefFieldAndLocalAddresses() {
            using var module = CreateModuleWithResolver("USP.DelegatePlaceholderProcessor.ByRef");

            var rootContextDef = new TypeDefinition("UnifiedServerProcess", "RootContext",
                TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(rootContextDef);

            var hookDel = CreateDelegateType(module, "Terraria.DataStructures", "HookDel",
                module.TypeSystem.Void, module.TypeSystem.Int32);
            var seedDel = CreateDelegateType(module, "Terraria.WorldBuilding", "SeedChangedDel",
                module.TypeSystem.Void, module.TypeSystem.Boolean);

            var placementHook = new TypeDefinition("Terraria.DataStructures", "PlacementHook",
                TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(placementHook);

            var placementHookCtor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) { HasThis = true };
            placementHookCtor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            placementHook.Methods.Add(placementHookCtor);

            var hookField = new FieldDefinition("hook", FieldAttributes.Public, hookDel);
            placementHook.Fields.Add(hookField);

            var placementHookCtorWithStore = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) { HasThis = true };
            placementHookCtorWithStore.Parameters.Add(new ParameterDefinition(hookDel));
            placementHookCtorWithStore.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            placementHookCtorWithStore.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            placementHookCtorWithStore.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            {
                var il = placementHookCtorWithStore.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldarg_1));
                il.Append(il.Create(OpCodes.Stfld, hookField));
                il.Append(il.Create(OpCodes.Ret));
            }
            placementHook.Methods.Add(placementHookCtorWithStore);

            var worldGenOption = new TypeDefinition("Terraria.WorldBuilding", "AWorldGenerationOption",
                TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(worldGenOption);
            worldGenOption.Fields.Add(new FieldDefinition("OnOptionStateChanged", FieldAttributes.Public | FieldAttributes.Static, seedDel));

            var host = new TypeDefinition("Tests", "Host",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var calleeFieldByRef = new MethodDefinition("CalleeFieldByRef",
                MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            calleeFieldByRef.Parameters.Add(new ParameterDefinition(new ByReferenceType(hookDel)));
            calleeFieldByRef.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            host.Methods.Add(calleeFieldByRef);

            var calleeLocalByRef = new MethodDefinition("CalleeLocalByRef",
                MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            calleeLocalByRef.Parameters.Add(new ParameterDefinition(new ByReferenceType(hookDel)));
            calleeLocalByRef.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            host.Methods.Add(calleeLocalByRef);

            var callerFieldByRef = new MethodDefinition("CallerFieldByRef",
                MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void) {
                Body = { InitLocals = true }
            };
            callerFieldByRef.Body.Variables.Add(new VariableDefinition(placementHook));
            {
                var il = callerFieldByRef.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Newobj, placementHookCtor));
                il.Append(il.Create(OpCodes.Stloc_0));
                il.Append(il.Create(OpCodes.Ldloc_0));
                il.Append(il.Create(OpCodes.Ldflda, hookField));
                il.Append(il.Create(OpCodes.Call, calleeFieldByRef));
                il.Append(il.Create(OpCodes.Ret));
            }
            host.Methods.Add(callerFieldByRef);

            var callerLocalByRef = new MethodDefinition("CallerLocalByRef",
                MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void) {
                Body = { InitLocals = true }
            };
            callerLocalByRef.Body.Variables.Add(new VariableDefinition(placementHook));
            callerLocalByRef.Body.Variables.Add(new VariableDefinition(hookDel));
            {
                var il = callerLocalByRef.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Newobj, placementHookCtor));
                il.Append(il.Create(OpCodes.Stloc_0));
                il.Append(il.Create(OpCodes.Ldloc_0));
                il.Append(il.Create(OpCodes.Ldfld, hookField));
                il.Append(il.Create(OpCodes.Stloc_1));
                il.Append(il.Create(OpCodes.Ldloca_S, callerLocalByRef.Body.Variables[1]));
                il.Append(il.Create(OpCodes.Call, calleeLocalByRef));
                il.Append(il.Create(OpCodes.Ret));
            }
            host.Methods.Add(callerLocalByRef);

            var analyzers = new AnalyzerGroups(new NullLogger(), module);
            var processor = new DelegatePlaceholderProcessor(rootContextDef, analyzers);
            var source = new FilterArgumentSource(module, []);

            processor.Apply(new TestComponent(new NullLogger()), ref source);
            processor.ClearJumpSitesCache();

            var rewrittenHookType = hookField.FieldType;
            Assert.NotEqual(hookDel.FullName, rewrittenHookType.FullName);

            Assert.Equal(rewrittenHookType.FullName, placementHookCtorWithStore.Parameters[0].ParameterType.FullName);

            Assert.IsType<ByReferenceType>(calleeFieldByRef.Parameters[0].ParameterType);
            Assert.Equal(rewrittenHookType.FullName, ((ByReferenceType)calleeFieldByRef.Parameters[0].ParameterType).ElementType.FullName);

            Assert.IsType<ByReferenceType>(calleeLocalByRef.Parameters[0].ParameterType);
            Assert.Equal(rewrittenHookType.FullName, ((ByReferenceType)calleeLocalByRef.Parameters[0].ParameterType).ElementType.FullName);

            Assert.Equal(rewrittenHookType.FullName, callerLocalByRef.Body.Variables[1].VariableType.FullName);
        }

        private static ModuleDefinition CreateModuleWithResolver(string name) {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(AppContext.BaseDirectory);
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(DefaultCollection<>).Assembly.Location)!);

            var parameters = new ModuleParameters {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver,
            };
            return ModuleDefinition.CreateModule(name, parameters);
        }

        private static TypeDefinition CreateDelegateType(
            ModuleDefinition module,
            string ns,
            string name,
            TypeReference returnType,
            params TypeReference[] parameters) {

            var multicastDelegate = module.ImportReference(typeof(MulticastDelegate));
            var del = new TypeDefinition(ns, name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                multicastDelegate);
            module.Types.Add(del);

            var ctor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true,
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
            };
            ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            del.Methods.Add(ctor);

            var invoke = new MethodDefinition("Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                returnType) {
                HasThis = true,
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
            };
            foreach (var p in parameters) {
                invoke.Parameters.Add(new ParameterDefinition(p));
            }
            del.Methods.Add(invoke);

            return del;
        }

        private sealed class TestComponent(ILogger logger) : LoggedComponent(logger)
        {
            public override string Name => "unit-test";
        }

        private sealed class NullLogger : ILogger
        {
            public void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0) { }
            public void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0) { }
            public void Progress(ILoggedComponent sender, int iteration, int progress, int total, string message, int indent = 0, params object[] args) { }
            public void Progress(ILoggedComponent sender, int progress, int total, string message, int indent = 0, params object[] args) { }
            public void Debug(ILoggedComponent sender, int indent, string log, params object[] args) { }
            public void Info(ILoggedComponent sender, int indent, string log, params object[] args) { }
            public void Warn(ILoggedComponent sender, int indent, string log, params object[] args) { }
            public void Error(ILoggedComponent sender, int indent, string log, Exception ex, params object[] args) { }
            public void Error(ILoggedComponent sender, int indent, string log, params object[] args) { }
            public void Fatal(ILoggedComponent sender, int indent, string log, params object[] args) { }
            public void Debug(ILoggedComponent sender, string log, params object[] args) { }
            public void Info(ILoggedComponent sender, string log, params object[] args) { }
            public void Warn(ILoggedComponent sender, string log, params object[] args) { }
            public void Error(ILoggedComponent sender, string log, Exception ex, params object[] args) { }
            public void Error(ILoggedComponent sender, string log, params object[] args) { }
            public void Fatal(ILoggedComponent sender, string log, params object[] args) { }
        }
    }
}
