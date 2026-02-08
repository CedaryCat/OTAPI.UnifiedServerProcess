using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class MonoModExtensionsGetIdentifierTests
    {
        [Fact]
        public void GetIdentifier_FormatsGenericIdentifierBySpec() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.Spec", ModuleKind.Dll);

            var type = new TypeDefinition("Spec.Namespace", "Type",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            type.GenericParameters.Add(new GenericParameter("T0", type));
            type.GenericParameters.Add(new GenericParameter("T1", type));
            module.Types.Add(type);

            var method = new MethodDefinition("Method",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            type.Methods.Add(method);

            method.GenericParameters.Add(new GenericParameter("M0", method));
            method.Parameters.Add(new ParameterDefinition(method.GenericParameters[0]));
            method.Parameters.Add(new ParameterDefinition(type.GenericParameters[1]));
            method.Parameters.Add(new ParameterDefinition(type.GenericParameters[0]));
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

            Assert.Equal(
                "Spec.Namespace.Type`2::Method`1(!!0,!1,!0,System.Int32)",
                method.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_IgnoresSpecifiedParameterType() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.Ignore", ModuleKind.Dll);

            var rootContext = new TypeDefinition("UnifiedServerProcess", "RootContext",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(rootContext);

            var host = new TypeDefinition("Spec", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var ctor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            ctor.Parameters.Add(new ParameterDefinition(rootContext));
            ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            host.Methods.Add(ctor);

            Assert.Equal(
                "Spec.Host::.ctor(System.Int32)",
                ctor.GetIdentifier(withDeclaring: true, rootContext));
        }

        [Fact]
        public void GetIdentifier_AppliesTypeMapAndForceByRef() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.Map", ModuleKind.Dll);

            var oldType = new TypeDefinition("Spec", "OldType",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(oldType);

            var host = new TypeDefinition("Spec", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var method = new MethodDefinition("MapParam",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition(oldType));
            host.Methods.Add(method);

            var typeNameMap = new Dictionary<string, string> {
                [oldType.FullName] = "Spec.NewType"
            };

            var makeByRefIfNot = new HashSet<int> { 0 };

            Assert.Equal(
                "Spec.Host::MapParam(Spec.NewType&)",
                method.GetIdentifier(withDeclaring: true, typeNameMap, makeByRefIfNot));
        }

        [Fact]
        public void GetIdentifier_FormatsNestedGenericInstanceParameterName() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.NestedGeneric", ModuleKind.Dll);

            var outer = new TypeDefinition("Spec.Namespace", "Type",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            outer.GenericParameters.Add(new GenericParameter("TOuter", outer));
            module.Types.Add(outer);

            var nested = new TypeDefinition(string.Empty, "NestType",
                TypeAttributes.NestedPublic | TypeAttributes.Class,
                module.TypeSystem.Object);
            nested.GenericParameters.Add(new GenericParameter("TNested", nested));
            outer.NestedTypes.Add(nested);

            var nestedLeaf = new TypeDefinition(string.Empty, "TypeC",
                TypeAttributes.NestedPublic | TypeAttributes.Class,
                module.TypeSystem.Object);
            nested.NestedTypes.Add(nestedLeaf);

            var concreteArg = new TypeDefinition("Spec.Namespace", "TypeB",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(concreteArg);

            var host = new TypeDefinition("Spec", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var method = new MethodDefinition("Nest",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            method.GenericParameters.Add(new GenericParameter("M0", method));
            method.GenericParameters.Add(new GenericParameter("M1", method));
            host.Methods.Add(method);

            var nestedInstance = new GenericInstanceType(nested);
            nestedInstance.GenericArguments.Add(method.GenericParameters[1]);
            nestedInstance.GenericArguments.Add(concreteArg);

            var nestedLeafRef = new TypeReference(string.Empty, nestedLeaf.Name, module, module) {
                DeclaringType = nestedInstance
            };

            method.Parameters.Add(new ParameterDefinition(nestedLeafRef));

            Assert.Equal(
                "Spec.Host::Nest`2(Spec.Namespace.Type<!!1>.NestType<Spec.Namespace.TypeB>.TypeC)",
                method.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_UsesElementTypeForGenericDeclaringTypeReference() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.DeclaringGeneric", ModuleKind.Dll);

            var container = new TypeDefinition("Spec.Namespace", "Container",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            container.GenericParameters.Add(new GenericParameter("T", container));
            module.Types.Add(container);

            var closedContainer = new GenericInstanceType(container);
            closedContainer.GenericArguments.Add(module.TypeSystem.Int32);

            var methodRef = new MethodReference("Call",
                module.TypeSystem.Void,
                closedContainer);

            Assert.Equal("Spec.Namespace.Container`1::Call()", methodRef.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_ThrowsOnMalformedGenericMetadataByDefault() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.Malformed.Throw", ModuleKind.Dll);

            var host = new TypeDefinition("Spec", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var method = new MethodDefinition("M",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            host.Methods.Add(method);

            var malformedTypeRef = new TypeReference("Spec", "Malformed`1", module, module);
            var malformedInstance = new GenericInstanceType(malformedTypeRef);
            method.Parameters.Add(new ParameterDefinition(malformedInstance));

            Assert.Throws<InvalidOperationException>(() => method.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_FallbacksOnMalformedGenericMetadataWhenConfigured() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.Malformed.Fallback", ModuleKind.Dll);

            var host = new TypeDefinition("Spec", "Host",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(host);

            var method = new MethodDefinition("M",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            host.Methods.Add(method);

            var malformedTypeRef = new TypeReference("Spec", "Malformed`1", module, module);
            var malformedInstance = new GenericInstanceType(malformedTypeRef);
            method.Parameters.Add(new ParameterDefinition(malformedInstance));

            var originalFlag = MonoModExtensions.ThrowOnGetIdentifierMetadataMismatch;
            try {
                MonoModExtensions.ThrowOnGetIdentifierMetadataMismatch = false;
                Assert.Equal("Spec.Host::M(Spec.Malformed<!0>)", method.GetIdentifier());
            }
            finally {
                MonoModExtensions.ThrowOnGetIdentifierMetadataMismatch = originalFlag;
            }
        }

        [Fact]
        public void GetIdentifier_FormatsAnonymousCtorWithNamedParameters() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.AnonymousCtor", ModuleKind.Dll);

            var anonymousType = new TypeDefinition("Spec", "<>f__AnonymousType0",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(anonymousType);

            var ctorA = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            ctorA.Parameters.Add(new ParameterDefinition("alpha", ParameterAttributes.None, module.TypeSystem.Int32));
            anonymousType.Methods.Add(ctorA);

            var ctorB = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            ctorB.Parameters.Add(new ParameterDefinition("beta", ParameterAttributes.None, module.TypeSystem.Int32));
            anonymousType.Methods.Add(ctorB);

            Assert.Equal(
                "Spec.<>f__AnonymousType0::.ctor(alpha:System.Int32)",
                ctorA.GetIdentifier());

            Assert.Equal(
                "Spec.<>f__AnonymousType0::.ctor(beta:System.Int32)",
                ctorB.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_UsesResolvedAnonymousCtorParameterNamesWhenReferenceNamesAreEmpty() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.AnonymousCtor.Ref", ModuleKind.Dll);

            var anonymousType = new TypeDefinition("Spec", "<>f__AnonymousType1",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(anonymousType);

            var ctorA = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            ctorA.Parameters.Add(new ParameterDefinition("alpha", ParameterAttributes.None, module.TypeSystem.Int32));
            anonymousType.Methods.Add(ctorA);

            var ctorB = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            ctorB.Parameters.Add(new ParameterDefinition("beta", ParameterAttributes.None, module.TypeSystem.Int32));
            anonymousType.Methods.Add(ctorB);

            var ctorRef = new MethodReference(".ctor", module.TypeSystem.Void, anonymousType) {
                HasThis = true
            };
            ctorRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

            Assert.Equal(
                "Spec.<>f__AnonymousType1::.ctor(alpha:System.Int32)",
                ctorRef.GetIdentifier());
        }

        [Fact]
        public void GetIdentifier_DoesNotTreatBacktickInsideNameAsGenericArity() {
            using var module = ModuleDefinition.CreateModule("USP.GetIdentifier.BacktickInName", ModuleKind.Dll);

            var typeA = new TypeDefinition("HookEvents.Terraria.Main", "EntitySpriteDraw_Texture2D_Vector2_Nullable`1_Color_A",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(typeA);

            var typeB = new TypeDefinition("HookEvents.Terraria.Main", "EntitySpriteDraw_Texture2D_Vector2_Nullable`1_Color_B",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(typeB);

            var ctorA = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            typeA.Methods.Add(ctorA);

            var ctorB = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void) {
                HasThis = true
            };
            typeB.Methods.Add(ctorB);

            var idA = ctorA.GetIdentifier();
            var idB = ctorB.GetIdentifier();

            Assert.Equal(
                "HookEvents.Terraria.Main.EntitySpriteDraw_Texture2D_Vector2_Nullable`1_Color_A::.ctor()",
                idA);
            Assert.Equal(
                "HookEvents.Terraria.Main.EntitySpriteDraw_Texture2D_Vector2_Nullable`1_Color_B::.ctor()",
                idB);
            Assert.NotEqual(idA, idB);
        }
    }
}
