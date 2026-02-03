using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OTAPI.UnifiedServerProcess.UnitTests
{
    public class AnalysisRemapTests
    {
        [Fact]
        public void RemapModeMethodStackKey_RemapsParamAndLeavesField() {
            var oldToNew = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["A.M(System.Int32)"] = "A.M(System.String)",
            };

            Assert.Equal(
                "Param#A.M(System.String)→p",
                AnalysisRemap.RemapModeMethodStackKey("Param#A.M(System.Int32)→p", oldToNew)
            );

            Assert.Equal(
                "Field#Some.Type→SomeField",
                AnalysisRemap.RemapModeMethodStackKey("Field#Some.Type→SomeField", oldToNew)
            );
        }

        [Fact]
        public void RemapDictionaryKeysInPlace_HandlesSwaps() {
            var dict = new Dictionary<string, int>(StringComparer.Ordinal) {
                ["A"] = 1,
                ["B"] = 2,
            };

            var map = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["A"] = "B",
                ["B"] = "A",
            };

            AnalysisRemap.RemapDictionaryKeysInPlace(dict, map, "swap");

            Assert.Equal(2, dict.Count);
            Assert.Equal(1, dict["B"]);
            Assert.Equal(2, dict["A"]);
        }

        [Fact]
        public void ParameterTraceCollection_RemapKeys_MergesCollisions() {
            using var module = ModuleDefinition.CreateModule("USP.Remap.ParamTrace", ModuleKind.Dll);

            var p1 = new ParameterDefinition("p1", ParameterAttributes.None, module.TypeSystem.Int32);
            var p2 = new ParameterDefinition("p2", ParameterAttributes.None, module.TypeSystem.Int32);

            var trace1 = new AggregatedParameterProvenance();
            trace1.ReferencedParameters["x"] = new ParameterProvenance(p1, Enumerable.Empty<ParameterTracingChain>());

            var trace2 = new AggregatedParameterProvenance();
            trace2.ReferencedParameters["y"] = new ParameterProvenance(p2, Enumerable.Empty<ParameterTracingChain>());

            var collection = new ParameterTraceCollection<string>();
            Assert.True(collection.TryAddTrace("Param#Old→p", trace1));
            Assert.True(collection.TryAddTrace("Param#New→p", trace2));

            var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Old"] = "New" };
            collection.RemapKeys(key => AnalysisRemap.RemapModeMethodStackKey(key, map));

            Assert.True(collection.TryGetTrace("Param#New→p", out var merged));
            Assert.NotNull(merged);
            Assert.True(merged.ReferencedParameters.ContainsKey("x"));
            Assert.True(merged.ReferencedParameters.ContainsKey("y"));
        }

        [Fact]
        public void StaticFieldTraceCollection_RemapKeys_MergesCollisions() {
            using var module = ModuleDefinition.CreateModule("USP.Remap.StaticTrace", ModuleKind.Dll);

            var type = new TypeDefinition("T", "C", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);

            var f1 = new FieldDefinition("F1", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
            var f2 = new FieldDefinition("F2", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
            type.Fields.Add(f1);
            type.Fields.Add(f2);

            var trace1 = new AggregatedStaticFieldProvenance();
            trace1.TracedStaticFields[f1.GetIdentifier()] = new StaticFieldProvenance(f1, Enumerable.Empty<StaticFieldTracingChain>());

            var trace2 = new AggregatedStaticFieldProvenance();
            trace2.TracedStaticFields[f2.GetIdentifier()] = new StaticFieldProvenance(f2, Enumerable.Empty<StaticFieldTracingChain>());

            var collection = new StaticFieldTraceCollection<string>();
            Assert.True(collection.TryAddTrace("Param#Old→p", trace1));
            Assert.True(collection.TryAddTrace("Param#New→p", trace2));

            var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["Old"] = "New" };
            collection.RemapKeys(key => AnalysisRemap.RemapModeMethodStackKey(key, map));

            Assert.True(collection.TryGetTrace("Param#New→p", out var merged));
            Assert.NotNull(merged);
            Assert.True(merged.TracedStaticFields.ContainsKey(f1.GetIdentifier()));
            Assert.True(merged.TracedStaticFields.ContainsKey(f2.GetIdentifier()));
        }

        [Fact]
        public void MethodSignatureUpdateSession_BuildsMapping_ForMultipleParameters() {
            using var module = ModuleDefinition.CreateModule("USP.Remap.Session", ModuleKind.Dll);

            var type = new TypeDefinition("T", "C", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);

            var method = new MethodDefinition("M", MethodAttributes.Public, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Object));
            type.Methods.Add(method);

            var oldId = method.GetIdentifier();

            var session = new MethodSignatureUpdateSession();
            session.PlanParameterTypeChange(method, 0, module.TypeSystem.String);
            session.PlanParameterTypeChange(method, 1, module.TypeSystem.Int32);

            method.Parameters[0].ParameterType = module.TypeSystem.String;
            method.Parameters[1].ParameterType = module.TypeSystem.Int32;

            var map = session.BuildOldToNewMethodIdMapAndValidate();

            Assert.True(map.TryGetValue(oldId, out var newId));
            Assert.Equal(method.GetIdentifier(), newId);
        }

        [Fact]
        public void MethodSignatureUpdateSession_Throws_OnUnexpectedParameterTypeChange() {
            using var module = ModuleDefinition.CreateModule("USP.Remap.Session.Throw", ModuleKind.Dll);

            var type = new TypeDefinition("T", "C", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);

            var method = new MethodDefinition("M", MethodAttributes.Public, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Object));
            type.Methods.Add(method);

            var session = new MethodSignatureUpdateSession();
            session.PlanParameterTypeChange(method, 0, module.TypeSystem.String);

            method.Parameters[0].ParameterType = module.TypeSystem.String;
            method.Parameters[1].ParameterType = module.TypeSystem.Int32;

            Assert.Throws<InvalidOperationException>(() => session.BuildOldToNewMethodIdMapAndValidate());
        }
    }
}
