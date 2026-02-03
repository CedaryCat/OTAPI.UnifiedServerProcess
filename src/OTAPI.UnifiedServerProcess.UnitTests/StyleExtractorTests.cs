using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core;
using System.Reflection;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class StyleExtractorTests
    {
        [Fact]
        public void TypeDomain_UnionWith_PreservesInformation() {
            var styleExtractorType = typeof(PatchProjHookSets).GetNestedType("StyleExtractor", BindingFlags.NonPublic);
            Assert.NotNull(styleExtractorType);

            var typeDomainType = styleExtractorType!.GetNestedType("TypeDomain", BindingFlags.Public);
            Assert.NotNull(typeDomainType);

            var universe = typeDomainType!.GetMethod("Universe", BindingFlags.Public | BindingFlags.Static);
            var fromSingle = typeDomainType.GetMethod("FromSingle", BindingFlags.Public | BindingFlags.Static);
            var applyNeq = typeDomainType.GetMethod("ApplyNeq", BindingFlags.Public | BindingFlags.Instance);
            var unionWith = typeDomainType.GetMethod("UnionWith", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(universe);
            Assert.NotNull(fromSingle);
            Assert.NotNull(applyNeq);
            Assert.NotNull(unionWith);

            var u = universe!.Invoke(null, null)!; // ANY
            applyNeq!.Invoke(u, [1]);
            applyNeq.Invoke(u, [2]);               // ANY \ {1,2}

            var p1 = fromSingle!.Invoke(null, [1])!; // {1}

            // (ANY \ {1,2}) ∪ {1} = ANY \ {2}
            unionWith!.Invoke(u, [p1]);
            Assert.Equal(@"ANY \ {2}", u.ToString());

            var a = fromSingle.Invoke(null, [1])!;
            var b = fromSingle.Invoke(null, [2])!;
            unionWith.Invoke(a, [b]);
            Assert.Equal("{1,2}", a.ToString());

            var p = fromSingle.Invoke(null, [1])!;
            var u2 = universe.Invoke(null, null)!;
            applyNeq.Invoke(u2, [1]);
            applyNeq.Invoke(u2, [2]); // ANY \ {1,2}
            unionWith.Invoke(p, [u2]); // {1} ∪ (ANY \ {1,2}) = ANY \ {2}
            Assert.Equal(@"ANY \ {2}", p.ToString());
        }

        [Fact]
        public void Extract_Terminates_OnBackEdge() {
            var extractorType = typeof(PatchProjHookSets).GetNestedType("StyleExtractor", BindingFlags.NonPublic);
            Assert.NotNull(extractorType);

            var extract = extractorType!.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(extract);

            using var module = ModuleDefinition.CreateModule("USP.StyleExtractor.Termination", ModuleKind.Dll);

            var type = new TypeDefinition(
                @namespace: "Test",
                name: "C",
                attributes: Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                baseType: module.TypeSystem.Object
            );
            module.Types.Add(type);

            var method = new MethodDefinition(
                name: "M",
                attributes: Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                returnType: module.TypeSystem.Void
            );
            type.Methods.Add(method);

            var il = method.Body.GetILProcessor();
            var loopStart = Instruction.Create(OpCodes.Nop);
            il.Append(loopStart);
            il.Append(Instruction.Create(OpCodes.Br_S, loopStart));
            il.Append(Instruction.Create(OpCodes.Ret));

            var rules = extract!.Invoke(null, [method]);
            Assert.NotNull(rules);
        }
    }
}
