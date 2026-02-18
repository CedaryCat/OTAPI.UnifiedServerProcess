using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.IO;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class ParamModificationAnalyzerValueTypeMutationTests
    {
        [Fact]
        public void ParamModificationAnalyzer_DetectsValueTypeFieldWriteThroughAddress() {
            using var module = CreateModuleWithResolver("USP.ParamModification.ValueTypeWrite");

            var valueType = new TypeDefinition(
                "Tests",
                "Payload",
                TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                module.ImportReference(typeof(ValueType)));
            module.Types.Add(valueType);
            var valueField = new FieldDefinition("Number", FieldAttributes.Public, module.TypeSystem.Int32);
            valueType.Fields.Add(valueField);

            var holderType = new TypeDefinition(
                "Tests",
                "Holder",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(holderType);
            var payloadField = new FieldDefinition("PayloadField", FieldAttributes.Public, valueType);
            holderType.Fields.Add(payloadField);

            var hostType = new TypeDefinition(
                "Tests",
                "Host",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(hostType);

            var mutateMethod = new MethodDefinition(
                "Mutate",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            mutateMethod.Parameters.Add(new ParameterDefinition("holder", ParameterAttributes.None, holderType));
            hostType.Methods.Add(mutateMethod);

            var il = mutateMethod.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldflda, payloadField));
            il.Append(il.Create(OpCodes.Ldc_I4, 42));
            il.Append(il.Create(OpCodes.Stfld, valueField));
            il.Append(il.Create(OpCodes.Ret));

            var analyzers = new AnalyzerGroups(new NullLogger(), module);
            var methodId = mutateMethod.GetIdentifier();

            Assert.True(analyzers.ParamModificationAnalyzer.ModifiedParameters.TryGetValue(methodId, out var methodMutations));
            Assert.True(methodMutations.TryGetValue(0, out var parameterMutations));
            Assert.Contains(parameterMutations.Mutations, m =>
                m.ModificationAccessPath.Length == 2
                && m.ModificationAccessPath[0].Name == payloadField.Name
                && m.ModificationAccessPath[1].Name == valueField.Name);
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
