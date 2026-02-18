using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis.DataModels.MemberAccess;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class MemberAccessOperationModeTests
    {
        [Fact]
        public void ParameterTracingChain_ReadRejectsValueTypeMember_ButGetAddressAndWriteAccept() {
            using var module = ModuleDefinition.CreateModule("USP.MemberAccess.Mode.Param", ModuleKind.Dll);
            CreateContainerTypes(module, out var containerType, out var valueField, out _);

            var parameter = new ParameterDefinition("p", ParameterAttributes.None, containerType);
            var chain = new ParameterTracingChain(parameter, [], []);

            Assert.False(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.Read, out _));
            Assert.False(chain.TryExtendTracingWithMemberAccess(valueField, out _));

            Assert.True(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.GetAddress, out var byAddress));
            Assert.NotNull(byAddress);
            Assert.Single(byAddress.ComponentAccessPath);
            Assert.Equal(valueField.Name, byAddress.ComponentAccessPath[0].Name);

            Assert.True(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.Write, out var byWrite));
            Assert.NotNull(byWrite);
            Assert.Single(byWrite.ComponentAccessPath);
            Assert.Equal(valueField.Name, byWrite.ComponentAccessPath[0].Name);
        }

        [Fact]
        public void ParameterTracingChain_LegacyExtend_EqualsReadMode_ForReferenceMember() {
            using var module = ModuleDefinition.CreateModule("USP.MemberAccess.Mode.Param.Legacy", ModuleKind.Dll);
            CreateContainerTypes(module, out var containerType, out _, out var referenceField);

            var parameter = new ParameterDefinition("p", ParameterAttributes.None, containerType);
            var chain = new ParameterTracingChain(parameter, [], []);

            bool readOk = chain.TryApplyMemberAccess(referenceField, MemberAccessOperation.Read, out var readResult);
            bool legacyOk = chain.TryExtendTracingWithMemberAccess(referenceField, out var legacyResult);

            Assert.Equal(readOk, legacyOk);
            Assert.NotNull(readResult);
            Assert.NotNull(legacyResult);
            Assert.Equal(readResult.ComponentAccessPath.Length, legacyResult.ComponentAccessPath.Length);
            Assert.Equal(readResult.ComponentAccessPath[0].Name, legacyResult.ComponentAccessPath[0].Name);
        }

        [Fact]
        public void StaticFieldTracingChain_ReadRejectsValueTypeMember_ButGetAddressAndWriteAccept() {
            using var module = ModuleDefinition.CreateModule("USP.MemberAccess.Mode.Static", ModuleKind.Dll);
            CreateContainerTypes(module, out var containerType, out var valueField, out _);

            var host = new TypeDefinition("Tests", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);
            var tracedStaticField = new FieldDefinition("Root", FieldAttributes.Public | FieldAttributes.Static, containerType);
            host.Fields.Add(tracedStaticField);

            var chain = new StaticFieldTracingChain(tracedStaticField, [], []);

            Assert.False(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.Read, out _));
            Assert.False(chain.TryExtendTracingWithMemberAccess(valueField, out _));

            Assert.True(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.GetAddress, out var byAddress));
            Assert.NotNull(byAddress);
            Assert.Single(byAddress.ComponentAccessPath);
            Assert.Equal(valueField.Name, byAddress.ComponentAccessPath[0].Name);

            Assert.True(chain.TryApplyMemberAccess(valueField, MemberAccessOperation.Write, out var byWrite));
            Assert.NotNull(byWrite);
            Assert.Single(byWrite.ComponentAccessPath);
            Assert.Equal(valueField.Name, byWrite.ComponentAccessPath[0].Name);
        }

        [Fact]
        public void StaticFieldTracingChain_LegacyExtend_EqualsReadMode_ForReferenceMember() {
            using var module = ModuleDefinition.CreateModule("USP.MemberAccess.Mode.Static.Legacy", ModuleKind.Dll);
            CreateContainerTypes(module, out var containerType, out _, out var referenceField);

            var host = new TypeDefinition("Tests", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);
            var tracedStaticField = new FieldDefinition("Root", FieldAttributes.Public | FieldAttributes.Static, containerType);
            host.Fields.Add(tracedStaticField);

            var chain = new StaticFieldTracingChain(tracedStaticField, [], []);

            bool readOk = chain.TryApplyMemberAccess(referenceField, MemberAccessOperation.Read, out var readResult);
            bool legacyOk = chain.TryExtendTracingWithMemberAccess(referenceField, out var legacyResult);

            Assert.Equal(readOk, legacyOk);
            Assert.NotNull(readResult);
            Assert.NotNull(legacyResult);
            Assert.Equal(readResult.ComponentAccessPath.Length, legacyResult.ComponentAccessPath.Length);
            Assert.Equal(readResult.ComponentAccessPath[0].Name, legacyResult.ComponentAccessPath[0].Name);
        }

        private static void CreateContainerTypes(
            ModuleDefinition module,
            out TypeDefinition containerType,
            out FieldDefinition valueField,
            out FieldDefinition referenceField) {

            var payloadType = new TypeDefinition(
                "Tests",
                "Payload",
                TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                module.ImportReference(typeof(ValueType)));
            module.Types.Add(payloadType);
            payloadType.Fields.Add(new FieldDefinition("Number", FieldAttributes.Public, module.TypeSystem.Int32));

            containerType = new TypeDefinition(
                "Tests",
                "Container",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(containerType);

            valueField = new FieldDefinition("PayloadField", FieldAttributes.Public, payloadType);
            referenceField = new FieldDefinition("ReferenceField", FieldAttributes.Public, module.TypeSystem.Object);
            containerType.Fields.Add(valueField);
            containerType.Fields.Add(referenceField);
        }
    }
}
