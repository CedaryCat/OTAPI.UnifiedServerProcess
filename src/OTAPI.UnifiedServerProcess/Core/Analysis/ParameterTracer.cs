using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NuGet.Protocol.Plugins;
using OTAPI.MultiServerCore.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.MultiServerCore.Tracers {
    public class ParamFlowMethodData {
        public readonly MethodDefinition Method;
        public readonly ParamTracingValue? ReturnVariable;
        public readonly TracingValueCollection<string> ParameterValues;
        public readonly TracingValueCollection<VariableDefinition> LocalVariables;
        public readonly TracingValueCollection<string> StackValues;

        public static string GetStackKey(MethodDefinition method, Instruction instruction) {
            if (instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld) {
                var field = (FieldReference)instruction.Operand;
                return $"Field:{field.DeclaringType.FullName}:{field.Name}";
            }
            if (MonoModCommon.IL.TryGetReferencedParameter(method, instruction, out var parameter)) {
                return $"Param:{method.GetIdentifier()}:{(string.IsNullOrEmpty(parameter.Name) ? "this" : parameter.Name)}";
            }
            if (method.HasBody) {
                if (MonoModCommon.IL.TryGetReferencedVariable(method, instruction, out var variable)) {
                    return $"Variable:{method.GetIdentifier()}:V_{variable.Index}";
                }
            }
            return $"Others:{method.GetIdentifier()}:IL_{instruction.Offset}";
        }
        public ParamFlowMethodData(
            MethodDefinition method, 
            ParamTracingValue? returnVariable, 
            TracingValueCollection<string> parameterValues, 
            TracingValueCollection<VariableDefinition> localVariables, 
            TracingValueCollection<string> stackValues) {

            Method = method;
            ReturnVariable = returnVariable;
            ParameterValues = parameterValues;
            LocalVariables = localVariables;
            StackValues = stackValues;
        }
    }
    public class ParamTracingValue {
        public ParamTracingValue() { }
        public ParamTracingValue(ParamTracingValue value, FieldReference setsField) {
            ContainingParts = value.ContainingParts.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(v => new ParamPartsContainer(v, setsField)).ToHashSet());
        }
        public ParamTracingValue(ParamTracingValue value, PropertyReference setsProperty) {
            ContainingParts = value.ContainingParts.ToDictionary(
                kv => kv.Key, 
                kv => kv.Value.Select(v => new ParamPartsContainer(v, setsProperty)).ToHashSet());
        }
        public bool TraceLoadMember(MemberReference member, [NotNullWhen(true)] out ParamTracingValue? newValue) {
            newValue = new ParamTracingValue();
            foreach (var kv in ContainingParts) {
                foreach (var item in kv.Value) {
                    if (item.TryTraceLoadMember(member, out var container)) {
                        if (!newValue.ContainingParts.TryGetValue(kv.Key, out var hashset)) {
                            hashset = new HashSet<ParamPartsContainer>();
                        }
                        hashset.Add(container);
                    }
                }
            }
            if (newValue.ContainingParts.Count == 0) {
                newValue = null;
                return false;
            }
            return true;
        }
        public readonly Dictionary<string, HashSet<ParamPartsContainer>> ContainingParts = new();
    }
    public class ParamPartsContainer {
        public readonly ParameterDefinition ComesFrom;
        public readonly MemberReference[] NestedChain;
        public ParamPartsContainer(ParameterDefinition comesFrom, MemberReference[] nestedChain) {
            ComesFrom = comesFrom;
            NestedChain = nestedChain;
        }

        public override string ToString() {
            if (NestedChain.Length == 0) {
                return "[" + (string.IsNullOrEmpty(ComesFrom.Name) ? "this" : ComesFrom.Name) + "]";
            }
            return "[" + (string.IsNullOrEmpty(ComesFrom.Name) ? "this" : ComesFrom.Name) + "] = " + string.Join(".", NestedChain.Select(x => x.Name));
        }
        public override int GetHashCode() {
            return ToString().GetHashCode();
        }
        public override bool Equals(object? obj) {
            if (obj is not ParamPartsContainer other) return false;
            return ToString() == other.ToString();
        }

        public ParamPartsContainer(ParamPartsContainer value, FieldReference setsField) {
            ComesFrom = value.ComesFrom;
            NestedChain = [setsField, .. value.NestedChain];
        }
        public ParamPartsContainer(ParamPartsContainer value, PropertyReference setsProperty) {
            ComesFrom = value.ComesFrom;
            NestedChain = [setsProperty, .. value.NestedChain];
        }

        public bool TryTraceLoadMember(MemberReference member, [NotNullWhen(true)] out ParamPartsContainer? newContainer) {
            newContainer = null;
            if (NestedChain.Length == 0) {
                bool vaildMember = false;
                if (member is MethodReference m) {
                    vaildMember = !m.ReturnType.IsValueType;
                }
                else if (member is FieldReference f) {
                    vaildMember = !f.FieldType.IsValueType;
                }
                else if (member is PropertyReference p) {
                    vaildMember = !p.PropertyType.IsValueType;
                }
                if (vaildMember) {
                    newContainer = new ParamPartsContainer(ComesFrom, []);
                    return true;
                }
                return false;
            }
            var last = NestedChain.Last();
            if (member.FullName == last.FullName) {
                newContainer = new ParamPartsContainer(ComesFrom, [.. NestedChain.Skip(1)]);
                return true;
            }
            return false;
        }
    }
    public class TracingValueCollection<TKey> where TKey : notnull {
        readonly Dictionary<TKey, ParamTracingValue> values = new();
        public bool TryGetValue(TKey key, [NotNullWhen(true)] out ParamTracingValue? value) 
            => values.TryGetValue(key, out value);
        public bool TryAdd(TKey key, ParamTracingValue value) {
            if (values.TryGetValue(key, out var oldValue)) {
                bool added = false;
                foreach (var kv in value.ContainingParts) {
                    if (oldValue.ContainingParts.TryAdd(kv.Key, kv.Value)) {
                        added = true;
                    }
                }
                return added;
            }
            values.Add(key, value);
            return true;
        }
        public bool TryAddPart(TKey key, ParamPartsContainer part) {
            if (values.TryGetValue(key, out var oldValue)) {
                if (!oldValue.ContainingParts.TryGetValue(part.ComesFrom.Name, out var hashset)) {
                    hashset = [];
                }
                if (hashset.Add(part)) {
                    return true;
                }
                return false;
            }
            var value = new ParamTracingValue();
            value.ContainingParts.Add(part.ComesFrom.Name, [part]);
            values.Add(key, value);
            return true;
        }
        public IEnumerator<ParamTracingValue> GetEnumerator() => values.Values.GetEnumerator();
        public int Count => values.Count;
    }
    public class ParameterTracer {
        public readonly ImmutableDictionary<string, ParamFlowMethodData> TracedMethods;
        public ParameterTracer(ModuleDefinition module, MethodCallGraph methodCallTracer, MethodInheritanceGraph methodInheritanceTracer) {

            using FileStream fs = File.Create("PTLog.text");
            using var writer = new StreamWriter(fs);

            void Log(string item) {
                Console.WriteLine(item);
                writer.WriteLine(item);
            }

            var methodStackValues = new Dictionary<string, TracingValueCollection<string>>();
            var methodParameterValues = new Dictionary<string, TracingValueCollection<string>>();
            var methodLocalValues = new Dictionary<string, TracingValueCollection<VariableDefinition>>();
            var methodReturnValues = new TracingValueCollection<string>();

            int iterations = 0;
            var types = module.GetAllTypes().ToArray();

            Dictionary<string, MethodDefinition> cachedWorks = types
                .SelectMany(x => x.Methods)
                .Where(x => x.HasBody)
                .ToDictionary(x => x.GetIdentifier());

            do {
                iterations++;

                var works = cachedWorks.Values.ToArray();
                for (int progress = 0; progress < works.Length; progress++) {
                    var method = works[progress];

                    if (!method.HasBody) {
                        continue;
                    }

                    // Log($"[ParameterTracer|iteration:{iterations}, progress:{progress}/{works.Length}] {method.GetDebugName()}");

                    bool anyInnerChanged = false;
                    bool anyOuterChanged = false;

                    var jumpSources = MonoModCommon.Stack.BuildJumpSourceMap(method);

                    if (!methodStackValues.TryGetValue(method.GetIdentifier(), out var stackValues)) {
                        methodStackValues.Add(method.GetIdentifier(), stackValues = new());
                    }
                    if (!methodParameterValues.TryGetValue(method.GetIdentifier(), out var parameterValues)) {
                        methodParameterValues.Add(method.GetIdentifier(), parameterValues = new());
                    }
                    if (!methodLocalValues.TryGetValue(method.GetIdentifier(), out var localValues)) {
                        methodLocalValues.Add(method.GetIdentifier(), localValues = new());
                    }

                    do {
                        anyInnerChanged = false;

                        foreach (var instruction in method.Body.Instructions) {
                            switch (instruction.OpCode.Code) {
                                case Code.Ret: {
                                        if (method.ReturnType.FullName == module.TypeSystem.Void.FullName) {
                                            break;
                                        }

                                        foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSources)) {
                                            var loadReturnValue = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSources).First();

                                            if (stackValues.TryGetValue(ParamFlowMethodData.GetStackKey(method, loadReturnValue.RealPushValueInstruction), out var value)) {
                                                if (methodReturnValues.TryAdd(method.GetIdentifier(), value)) {
                                                    anyOuterChanged = true;
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case Code.Ldarg_0:
                                case Code.Ldarg_1:
                                case Code.Ldarg_2:
                                case Code.Ldarg_3:
                                case Code.Ldarg_S:
                                case Code.Ldarg: {
                                        var parameter = MonoModCommon.IL.GetReferencedParameter(method, instruction);

                                        if (parameter.ParameterType.IsValueType) {
                                            break;
                                        }
                                        if (stackValues.TryAddPart(ParamFlowMethodData.GetStackKey(method, instruction), new ParamPartsContainer(parameter, []))) {
                                            anyInnerChanged = true;
                                        }
                                        break;
                                    }
                                case Code.Stloc_0:
                                case Code.Stloc_1:
                                case Code.Stloc_2:
                                case Code.Stloc_3:
                                case Code.Stloc_S: {
                                        var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                                        if (variable.VariableType.IsValueType) {
                                            break;
                                        }
                                        foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSources)) {
                                            foreach (var loadValue in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSources)) {

                                                if (stackValues.TryGetValue(ParamFlowMethodData.GetStackKey(method, loadValue.RealPushValueInstruction), out var value)) {
                                                    if (localValues.TryAdd(variable, value)) {
                                                        anyInnerChanged = true;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case Code.Ldloc_0:
                                case Code.Ldloc_1:
                                case Code.Ldloc_2:
                                case Code.Ldloc_3:
                                case Code.Ldloc_S: {
                                        var variable = MonoModCommon.IL.GetReferencedVariable(method, instruction);
                                        if (variable.VariableType.IsValueType) {
                                            break;
                                        }
                                        if (localValues.TryGetValue(variable, out var value)) {
                                            if (stackValues.TryAdd(ParamFlowMethodData.GetStackKey(method, instruction), value)) {
                                                anyInnerChanged = true;
                                            }
                                        }
                                        break;
                                    }
                                case Code.Ldfld: {
                                        var field = (FieldReference)instruction.Operand;
                                        if (field.FieldType.IsValueType) {
                                            break;
                                        }
                                        foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSources)) {
                                            foreach (var loadObj in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSources)) {

                                                if (stackValues.TryGetValue(ParamFlowMethodData.GetStackKey(method, loadObj.RealPushValueInstruction), out var value) && value.TraceLoadMember(field, out var newValue)) {
                                                    if (stackValues.TryAdd(ParamFlowMethodData.GetStackKey(method, instruction), newValue)) {
                                                        anyInnerChanged = true;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case Code.Stfld: {

                                        var field = (FieldReference)instruction.Operand;
                                        if (field.FieldType.IsValueType) {
                                            break;
                                        }
                                        foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, instruction, jumpSources)) {
                                            // load instance
                                            var loadObj = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[0].Instructions.Last(), jumpSources).First();
                                            // load field value
                                            var loadValueForField = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[1].Instructions.Last(), jumpSources).First();

                                            // The instance currently has the field with this tracing value
                                            // so the parameter can be traced by order: 
                                            // parameter (reference parts) = chains before... -> field
                                            if (stackValues.TryGetValue(ParamFlowMethodData.GetStackKey(method, loadValueForField.RealPushValueInstruction), out var fieldValue)) {
                                                ParamTracingValue addedValue;
                                                if (stackValues.TryAdd(ParamFlowMethodData.GetStackKey(method, loadObj.RealPushValueInstruction), addedValue = new ParamTracingValue(fieldValue, field))) {
                                                    anyInnerChanged = true;
                                                }

                                                if (MonoModCommon.IL.TryGetReferencedParameter(method, loadObj.RealPushValueInstruction, out var param)) {
                                                    if ((param.Name == "this" || param == method.Body.ThisParameter)
                                                        && method.IsConstructor
                                                        && method.ReturnType.FullName == module.TypeSystem.Void.FullName
                                                        && methodReturnValues.TryAdd(method.GetIdentifier(), addedValue)) {

                                                        anyOuterChanged = true;
                                                    }
                                                    else if (parameterValues.TryAdd(param.Name, addedValue)) {
                                                        anyOuterChanged = true;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case Code.Call:
                                case Code.Callvirt:
                                case Code.Newobj: {
                                        var calleeBase = ((MethodReference)instruction.Operand).TryResolve();

                                        // Definition in unloaded assembly, just skip
                                        if (calleeBase is null) {
                                            break;
                                        }

                                        if ((calleeBase.IsStatic || calleeBase.IsConstructor) && calleeBase.Parameters.Count == 0) {
                                            break;
                                        }

                                        if (!methodInheritanceTracer.MethodImplementationChains.TryGetValue(calleeBase.GetIdentifier(), out var callees)) {
                                            break;
                                        }

                                        // TODO: use linq
                                        var paths = MonoModCommon.Stack.AnalyzeParametersSources(method, instruction, jumpSources);
                                        MonoModCommon.Stack.StackTopTypePath[][] loadParamsInEveryPaths = new MonoModCommon.Stack.StackTopTypePath[paths.Length][];

                                        for (int i = 0; i < loadParamsInEveryPaths.Length; i++) {
                                            var path = paths[i];
                                            loadParamsInEveryPaths[i] = new MonoModCommon.Stack.StackTopTypePath[path.ParametersSources.Length];
                                            for (int j = 0; j < path.ParametersSources.Length; j++) {
                                                loadParamsInEveryPaths[i][j] = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, path.ParametersSources[j].Instructions.Last(), jumpSources).First();
                                            }
                                        }

                                        foreach (var callee in callees) {

                                            methodReturnValues.TryGetValue(callee.GetIdentifier(), out var returnValue);

                                            foreach (var loadParams in loadParamsInEveryPaths) {
                                                for (int i = 0; i < loadParams.Length; i++) {

                                                    var paramIndexInCallee = i;
                                                    if (!callee.IsStatic && !callee.IsConstructor) {
                                                        paramIndexInCallee -= 1;
                                                    }

                                                    var paramInCallee = paramIndexInCallee == -1 ? callee.Body.ThisParameter : callee.Parameters[paramIndexInCallee];

                                                    if (paramIndexInCallee != -1 && paramInCallee.ParameterType.IsValueType) {
                                                        continue;
                                                    }

                                                    var loadValue = loadParams[i];
                                                    if (!stackValues.TryGetValue(ParamFlowMethodData.GetStackKey(method, loadValue.RealPushValueInstruction), out var loadingParamStackValue)) {
                                                        continue;
                                                    }

                                                    if (returnValue is not null) {
                                                        if (!returnValue.ContainingParts.TryGetValue(paramInCallee.Name, out var paramParts)) {
                                                            continue;
                                                        }

                                                        foreach (var outerPart in loadingParamStackValue.ContainingParts.Values.SelectMany(v => v).ToArray()) {
                                                            foreach (var innerPart in paramParts) {
                                                                var substitution = new ParamPartsContainer(outerPart.ComesFrom, [.. outerPart.NestedChain, .. innerPart.NestedChain]);
                                                                if (stackValues.TryAddPart(ParamFlowMethodData.GetStackKey(method, instruction), substitution)) {
                                                                    anyOuterChanged = true;
                                                                }
                                                            }
                                                        }
                                                    }

                                                    if (methodParameterValues.TryGetValue(callee.GetIdentifier(), out var calleeParameterValues)) {
                                                        foreach (var paramValue in calleeParameterValues) {
                                                            if (!paramValue.ContainingParts.TryGetValue(paramInCallee.Name, out var paramParts)) {
                                                                continue;
                                                            }

                                                            foreach (var outerPart in loadingParamStackValue.ContainingParts.Values.SelectMany(v => v).ToArray()) {
                                                                foreach (var innerPart in paramParts) {

                                                                    // Ignore self
                                                                    if (innerPart.ComesFrom.Name == paramInCallee.Name) {
                                                                        continue;
                                                                    }

                                                                    var substitution = new ParamPartsContainer(outerPart.ComesFrom, [.. outerPart.NestedChain, .. innerPart.NestedChain]);
                                                                    if (stackValues.TryAddPart(ParamFlowMethodData.GetStackKey(method, loadValue.RealPushValueInstruction), substitution)) {
                                                                        anyOuterChanged = true;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        break;
                                    }
                            }
                        }
                    }
                    while (anyInnerChanged);

                    cachedWorks.Remove(method.GetIdentifier());

                    if (anyOuterChanged) {
                        Log($"[ParameterTracer|iteration:{iterations}, progress:{progress}/{works.Length}] {method.GetDebugName()}");
                        foreach (var usedBy in methodCallTracer.methodCalls.TryGetValue(method.GetIdentifier(), out var calls) ? calls.UsedByMethods : []) {
                            if (cachedWorks.TryAdd(usedBy.GetIdentifier(), usedBy)) {
                                Log($"[ParameterTracer|iteration:{iterations}, progress:{progress}/{works.Length}]    Added {usedBy.GetDebugName()}");
                            }
                        }
                    }
                }
            }
            while (cachedWorks.Count > 0);

            var result = new Dictionary<string, ParamFlowMethodData>();
            foreach (var type in module.GetAllTypes()) {
                foreach (var method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    var key = method.GetIdentifier();

                    methodParameterValues.TryGetValue(key, out var parameterValues);
                    methodLocalValues.TryGetValue(key, out var localValues);
                    methodReturnValues.TryGetValue(key, out var returnValue);

                    parameterValues ??= new();
                    localValues ??= new();

                    if (parameterValues.Count > 0 || localValues.Count > 0 || returnValue is not null) {

                        methodStackValues.TryGetValue(key, out var stackValues);
                        stackValues ??= new();

                        result.Add(key, new ParamFlowMethodData(method, returnValue, parameterValues, localValues, stackValues));
                    }
                }
            }

            TracedMethods = result.ToImmutableDictionary();
        }
    }
}
