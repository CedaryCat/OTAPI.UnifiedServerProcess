using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public sealed class DelegatePlaceholderProcessor(AnalyzerGroups analyzers) : IGeneralArgProcessor, IJumpSitesCacheFeature
    {
        public string Name => nameof(DelegatePlaceholderProcessor);
        readonly AnalyzerGroups analyzers = analyzers ?? throw new ArgumentNullException(nameof(analyzers));

        public FieldReference[] FieldTasks(ModuleDefinition module) {
            return [
                module.GetType("Terraria.DataStructures.PlacementHook").GetField("hook"),
                module.GetType("Terraria.WorldBuilding.AWorldGenerationOption").GetField("OnOptionStateChanged"),
            ];
        }

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            ModuleDefinition module = source.MainModule;

            FieldDefinition[] taskFields = FieldTasks(source.MainModule)
                .Select(fr => fr.TryResolve() ?? throw new InvalidOperationException($"Failed to resolve field task '{fr.FullName}'."))
                .Distinct()
                .ToArray();

            if (taskFields.Length == 0) {
                return;
            }

            Tasks.GeneratedDelegateFactory.AddNameMapping("PlaceHookDele", Tasks.DelegateSignature.Capture(taskFields.Single(f => f.Name is "hook").FieldType));
            Tasks.GeneratedDelegateFactory.AddNameMapping("SeedChangedDele", Tasks.DelegateSignature.Capture(taskFields.Single(f => f.Name is "OnOptionStateChanged").FieldType));

            Dictionary<FieldDefinition, List<(MethodDefinition Method, Instruction Inst)>> fieldUseSites = Index.BuildFieldUseSites(module);
            Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> callUseSites = Index.BuildMethodCallSites(module);

            Tasks.DelegateTaskGroup[] taskGroups = Tasks.Build(module, taskFields);

            foreach (Tasks.DelegateTaskGroup group in taskGroups) {
                foreach (FieldDefinition field in group.Fields) {
                    field.FieldType = group.Transform.TransformType(field.FieldType);
                }
            }

            static TypeReference TransformByAll(TypeReference type, Tasks.DelegateTaskGroup[] groups) {
                TypeReference current = type;
                foreach (Tasks.DelegateTaskGroup g in groups) {
                    current = g.Transform.TransformType(current);
                }
                return current;
            }

            // Rewrite type operands (e.g. castclass/isinst/ldtoken) up-front to avoid relying on value-flow reaching them.
            foreach (TypeDefinition? type in module.GetAllTypes()) {
                foreach (MethodDefinition? method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (Instruction? inst in method.Body.Instructions) {
                        if (inst.Operand is not TypeReference tr) {
                            continue;
                        }
                        TypeReference transformed = TransformByAll(tr, taskGroups);
                        if (transformed.FullName != tr.FullName) {
                            inst.Operand = transformed;
                        }
                    }
                }
            }

            var session = new MethodSignatureUpdateSession();
            var inheritanceIndex = new InheritanceIndex(module, analyzers.MethodInheritanceGraph);
            var planner = new SignaturePlanner(module, session, inheritanceIndex);

            var engine = new Engine(
                module,
                fieldUseSites,
                callUseSites,
                planner,
                getJumpSites: this.GetMethodJumpSites
            );

            SynchronizeFieldLikeEvents(taskGroups, planner, callUseSites);

            engine.Run(taskGroups);
            analyzers.CommitMethodSignatureUpdate(session);
        }

        private static void SynchronizeFieldLikeEvents(
            Tasks.DelegateTaskGroup[] taskGroups,
            SignaturePlanner planner,
            Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> callUseSites) {

            ArgumentNullException.ThrowIfNull(taskGroups);
            ArgumentNullException.ThrowIfNull(planner);
            ArgumentNullException.ThrowIfNull(callUseSites);

            static void RewriteCallOperandsForSignatureChange(
                Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> callUseSites,
                MethodDefinition callee) {

                if (!callUseSites.TryGetValue(callee, out List<(MethodDefinition Caller, Instruction CallInst)>? callers)) {
                    return;
                }

                foreach ((MethodDefinition _, Instruction? callInst) in callers) {
                    if (callInst.Operand is not MethodReference callRef) {
                        continue;
                    }

                    callInst.Operand = MonoModCommon.Structure.CreateMethodReference(callRef, callee);
                }
            }

            void EnsureAccessorMatchesEventType(MethodDefinition? accessor, TypeReference eventType) {
                if (accessor is null || accessor.Parameters.Count != 1) {
                    return;
                }

                TypeReference current = accessor.Parameters[0].ParameterType;
                if (current.FullName == eventType.FullName) {
                    return;
                }

                MethodDefinition[] affected = planner.PlanParameterTypeChange(accessor, 0, eventType);
                foreach (MethodDefinition affectedMethod in affected) {
                    RewriteCallOperandsForSignatureChange(callUseSites, affectedMethod);
                }
            }

            foreach (Tasks.DelegateTaskGroup group in taskGroups) {
                foreach (FieldDefinition field in group.Fields) {
                    TypeDefinition? declaringType = field.DeclaringType;
                    if (declaringType is null || !declaringType.HasEvents) {
                        continue;
                    }

                    foreach (EventDefinition? evt in declaringType.Events) {
                        if (evt.Name != field.Name) {
                            continue;
                        }

                        TypeReference expectedEventType = field.FieldType;
                        if (evt.EventType.FullName != expectedEventType.FullName) {
                            evt.EventType = expectedEventType;
                        }

                        EnsureAccessorMatchesEventType(evt.AddMethod, expectedEventType);
                        EnsureAccessorMatchesEventType(evt.RemoveMethod, expectedEventType);
                    }
                }
            }
        }
        static class Index
        {

            public static Dictionary<FieldDefinition, List<(MethodDefinition Method, Instruction Inst)>> BuildFieldUseSites(ModuleDefinition module) {
                ArgumentNullException.ThrowIfNull(module);

                Dictionary<FieldDefinition, List<(MethodDefinition, Instruction)>> result = [];

                foreach (TypeDefinition? type in module.GetAllTypes()) {
                    foreach (MethodDefinition? method in type.Methods) {
                        if (!method.HasBody) {
                            continue;
                        }
                        foreach (Instruction? inst in method.Body.Instructions) {
                            if (inst.Operand is not FieldReference fr) {
                                continue;
                            }
                            FieldDefinition? def = fr.TryResolve();
                            if (def is null || def.Module != module) {
                                continue;
                            }
                            if (!result.TryGetValue(def, out List<(MethodDefinition, Instruction)>? list)) {
                                result.Add(def, list = []);
                            }
                            list.Add((method, inst));
                        }
                    }
                }

                return result;
            }

            public static Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> BuildMethodCallSites(ModuleDefinition module) {
                ArgumentNullException.ThrowIfNull(module);

                var result = new Dictionary<MethodDefinition, List<(MethodDefinition, Instruction)>>(ReferenceEqualityComparer.Instance);

                foreach (TypeDefinition? type in module.GetAllTypes()) {
                    foreach (MethodDefinition? method in type.Methods) {
                        if (!method.HasBody) {
                            continue;
                        }

                        foreach (Instruction? inst in method.Body.Instructions) {
                            if (inst.OpCode.Code is not Code.Call and not Code.Callvirt and not Code.Newobj) {
                                continue;
                            }
                            if (inst.Operand is not MethodReference mr) {
                                continue;
                            }
                            MethodDefinition? def = mr.TryResolve();
                            if (def is null || def.Module != module) {
                                continue;
                            }
                            if (!result.TryGetValue(def, out List<(MethodDefinition, Instruction)>? list)) {
                                result.Add(def, list = []);
                            }
                            list.Add((method, inst));
                        }
                    }
                }

                return result;
            }
        }

        class InheritanceIndex
        {

            readonly ModuleDefinition module;
            readonly Dictionary<MethodDefinition, List<MethodDefinition[]>> groupsByMethodDef = new(ReferenceEqualityComparer.Instance);

            public InheritanceIndex(ModuleDefinition module, MethodInheritanceGraph graph) {
                this.module = module ?? throw new ArgumentNullException(nameof(module));
                ArgumentNullException.ThrowIfNull(graph);

                foreach (KeyValuePair<string, MethodDefinition[]> kv in graph.RawMethodImplementationChains) {
                    MethodDefinition[] chain = kv.Value;
                    foreach (MethodDefinition m in chain) {
                        if (!groupsByMethodDef.TryGetValue(m, out List<MethodDefinition[]>? list)) {
                            groupsByMethodDef.Add(m, list = []);
                        }
                        list.Add(chain);
                    }
                }
            }

            public IEnumerable<MethodDefinition> GetRelatedMethods(MethodDefinition method) {
                if (!groupsByMethodDef.TryGetValue(method, out List<MethodDefinition[]>? groups)) {
                    yield return method;
                    yield break;
                }

                HashSet<MethodDefinition> emitted = new(ReferenceEqualityComparer.Instance);
                foreach (MethodDefinition[] group in groups) {
                    foreach (MethodDefinition m in group) {
                        if (m.Module != module) {
                            throw new NotSupportedException($"Inheritance group for '{method.GetIdentifier()}' includes external method '{m.GetIdentifier()}'.");
                        }
                        if (emitted.Add(m)) {
                            yield return m;
                        }
                    }
                }
            }
        }

        sealed class SignaturePlanner(
            ModuleDefinition module,
            MethodSignatureUpdateSession session,
            InheritanceIndex inheritanceIndex)
        {
            readonly ModuleDefinition module = module ?? throw new ArgumentNullException(nameof(module));
            readonly MethodSignatureUpdateSession session = session ?? throw new ArgumentNullException(nameof(session));
            readonly InheritanceIndex inheritanceIndex = inheritanceIndex ?? throw new ArgumentNullException(nameof(inheritanceIndex));

            public MethodDefinition[] PlanParameterTypeChange(MethodDefinition method, int parameterIndex, TypeReference newParameterType) {
                ArgumentNullException.ThrowIfNull(method);
                ArgumentNullException.ThrowIfNull(newParameterType);

                MethodDefinition[] affected = inheritanceIndex.GetRelatedMethods(method).ToArray();

                foreach (MethodDefinition? m in affected) {
                    if (m.Module != module) {
                        throw new NotSupportedException($"Cannot change signature for external method '{m.GetIdentifier()}'.");
                    }
                    if (parameterIndex < 0 || parameterIndex >= m.Parameters.Count) {
                        continue;
                    }

                    if (m.Parameters[parameterIndex].ParameterType.FullName == newParameterType.FullName) {
                        continue;
                    }

                    session.PlanParameterTypeChange(m, parameterIndex, newParameterType);
                    m.Parameters[parameterIndex].ParameterType = newParameterType;
                }

                return affected;
            }

            public MethodDefinition[] PlanReturnTypeChange(MethodDefinition method, TypeReference newReturnType) {
                ArgumentNullException.ThrowIfNull(method);
                ArgumentNullException.ThrowIfNull(newReturnType);

                MethodDefinition[] affected = inheritanceIndex.GetRelatedMethods(method).ToArray();

                foreach (MethodDefinition? m in affected) {
                    if (m.Module != module) {
                        throw new NotSupportedException($"Cannot change signature for external method '{m.GetIdentifier()}'.");
                    }

                    if (m.ReturnType.FullName == newReturnType.FullName) {
                        continue;
                    }

                    session.PlanReturnTypeChange(m, newReturnType);
                    m.ReturnType = newReturnType;
                }

                return affected;
            }
        }

        static class Tasks
        {

            public sealed class DelegateTaskGroup(DelegateSignature signature, DelegateTransform transform)
            {
                public readonly DelegateSignature Signature = signature;
                public readonly DelegateTransform Transform = transform;
                public readonly List<FieldDefinition> Fields = [];
            }

            public static DelegateTaskGroup[] Build(ModuleDefinition module, FieldDefinition[] fields) {
                ArgumentNullException.ThrowIfNull(module);
                ArgumentNullException.ThrowIfNull(fields);

                TypeDefinition container = GetOrCreateDelegateContainerType(module);
                var groups = new Dictionary<string, DelegateTaskGroup>(StringComparer.Ordinal);

                foreach (FieldDefinition field in fields) {
                    DelegateOccurrence occurrence = DelegateTypeScanner.ExtractSingleDelegateOccurrence(field.FieldType);
                    var signature = DelegateSignature.Capture(occurrence.DelegateType);
                    var key = signature.ToStableKeyString();

                    if (!groups.TryGetValue(key, out DelegateTaskGroup? group)) {
                        GeneratedDelegate generated = GeneratedDelegateFactory.GetOrCreateDelegateType(module, container, signature);
                        group = new DelegateTaskGroup(signature, new DelegateTransform(generated.NewDelegateTypeDef));
                        groups.Add(key, group);
                    }

                    group.Fields.Add(field);
                    group.Transform.OldDelegateFullNames.Add(occurrence.DelegateType.FullName);
                }

                return [.. groups.Values];
            }

            private static TypeDefinition GetOrCreateDelegateContainerType(ModuleDefinition module) {
                const string ns = Constants.DelegatesNameSpace;
                const string name = Constants.CtxDelegatesContainerName;
                TypeDefinition? existing = module.Types.FirstOrDefault(t => t.Namespace == ns && t.Name == name);
                if (existing is not null) {
                    return existing;
                }

                var container = new TypeDefinition(ns, name,
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                    module.TypeSystem.Object);
                module.Types.Add(container);
                return container;
            }

            public sealed class DelegateTransform(TypeDefinition newDelegateTypeDef)
            {
                public readonly TypeDefinition NewDelegateTypeDef = newDelegateTypeDef ?? throw new ArgumentNullException(nameof(newDelegateTypeDef));
                public readonly HashSet<string> OldDelegateFullNames = new(StringComparer.Ordinal);

                public TypeReference TransformType(TypeReference type) {
                    if (OldDelegateFullNames.Contains(type.FullName)) {
                        return MonoModCommon.Structure.CreateTypeReference(newDelegateTypeDef, newDelegateTypeDef.Module);
                    }

                    if (type is GenericInstanceType git) {
                        var anyChanged = false;
                        var newGit = new GenericInstanceType(git.ElementType);
                        foreach (TypeReference? arg in git.GenericArguments) {
                            TypeReference newArg = TransformType(arg);
                            if (newArg.FullName != arg.FullName) {
                                anyChanged = true;
                            }
                            newGit.GenericArguments.Add(newArg);
                        }
                        return anyChanged ? newGit : type;
                    }

                    if (type is ArrayType array) {
                        TypeReference newElement = TransformType(array.ElementType);
                        return newElement.FullName != array.ElementType.FullName ? new ArrayType(newElement, array.Rank) : type;
                    }

                    if (type is ByReferenceType byRef) {
                        TypeReference newElement = TransformType(byRef.ElementType);
                        return newElement.FullName != byRef.ElementType.FullName ? new ByReferenceType(newElement) : type;
                    }

                    if (type is PointerType ptr) {
                        TypeReference newElement = TransformType(ptr.ElementType);
                        return newElement.FullName != ptr.ElementType.FullName ? new PointerType(newElement) : type;
                    }

                    if (type is RequiredModifierType req) {
                        TypeReference newElement = TransformType(req.ElementType);
                        return newElement.FullName != req.ElementType.FullName ? new RequiredModifierType(req.ModifierType, newElement) : type;
                    }

                    if (type is OptionalModifierType opt) {
                        TypeReference newElement = TransformType(opt.ElementType);
                        return newElement.FullName != opt.ElementType.FullName ? new OptionalModifierType(opt.ModifierType, newElement) : type;
                    }

                    return type;
                }
            }

            public sealed record DelegateOccurrence(TypeReference DelegateType);

            public static class DelegateTypeScanner
            {
                public static DelegateOccurrence ExtractSingleDelegateOccurrence(TypeReference type) {
                    int count = 0;
                    TypeReference? found = null;

                    void Visit(TypeReference tr, int containerDepth) {
                        if (tr is null) {
                            return;
                        }

                        if (tr.IsDelegate() && tr.FullName is not "System.Delegate" and not "System.MulticastDelegate") {
                            if (containerDepth > 1) {
                                throw new NotSupportedException($"Nested container delegate types are not supported: '{type.FullName}'.");
                            }
                            count++;
                            found = tr;
                            return;
                        }

                        if (tr is GenericInstanceType git) {
                            foreach (TypeReference? arg in git.GenericArguments) {
                                Visit(arg, containerDepth + 1);
                            }
                            return;
                        }

                        if (tr is ArrayType array) {
                            Visit(array.ElementType, containerDepth + 1);
                            return;
                        }

                        if (tr is TypeSpecification spec) {
                            Visit(spec.ElementType, containerDepth);
                        }
                    }

                    Visit(type, 0);

                    if (count != 1 || found is null) {
                        throw new NotSupportedException($"Field type must contain exactly one delegate occurrence. Found {count} in '{type.FullName}'.");
                    }

                    if (found.ContainsGenericParameter) {
                        throw new NotSupportedException($"Delegate types with generic parameters are not supported: '{found.FullName}'.");
                    }

                    MethodDefinition? invoke = found.TryResolve()?.GetMethod("Invoke");
                    if (invoke is null) {
                        throw new InvalidOperationException($"Delegate '{found.FullName}' does not define Invoke.");
                    }

                    if (invoke.ReturnType.IsDelegate() || invoke.Parameters.Any(p => p.ParameterType.IsDelegate())) {
                        throw new NotSupportedException($"Nested delegate signatures are not supported: '{found.FullName}'.");
                    }

                    return new DelegateOccurrence(found);
                }
            }

            public sealed record DelegateSignature(TypeReference ReturnType, TypeReference[] ParameterTypes)
            {
                public static DelegateSignature Capture(TypeReference delegateType) {
                    ArgumentNullException.ThrowIfNull(delegateType);

                    TypeDefinition delDef = delegateType.TryResolve() ?? throw new InvalidOperationException($"Failed to resolve delegate type '{delegateType.FullName}'.");
                    MethodDefinition invoke = delDef.GetMethod("Invoke");

                    MonoModCommon.Structure.MapOption option = new();
                    if (delegateType is GenericInstanceType git) {
                        var map = new Dictionary<GenericParameter, TypeReference>();
                        for (int i = 0; i < git.GenericArguments.Count; i++) {
                            map.Add(git.ElementType.GenericParameters[i], git.GenericArguments[i]);
                        }
                        option = new MonoModCommon.Structure.MapOption(false, genericParameterMap: map);
                    }

                    TypeReference ret = MonoModCommon.Structure.DeepMapTypeReference(invoke.ReturnType, option);
                    TypeReference[] parameters = invoke.Parameters.Select(p => MonoModCommon.Structure.DeepMapTypeReference(p.ParameterType, option)).ToArray();

                    if (ret.ContainsGenericParameter || parameters.Any(p => p.ContainsGenericParameter)) {
                        throw new NotSupportedException($"Delegate signature must be closed. Found generic parameters in '{delegateType.FullName}'.");
                    }

                    return new DelegateSignature(ret, parameters);
                }

                public string ToStableKeyString() =>
                    $"{ReturnType.FullName}({string.Join(",", ParameterTypes.Select(p => p.FullName))})";
            }

            public sealed record GeneratedDelegate(TypeDefinition NewDelegateTypeDef);

            public static class GeneratedDelegateFactory
            {
                public static void AddNameMapping(string name, DelegateSignature signature) {
                    if (existingNames.Contains(name)) {
                        throw new InvalidOperationException();
                    }
                    nameMap[signature.ToStableKeyString()] = name;
                }
                static readonly Dictionary<string, string> nameMap = [];
                static readonly HashSet<string> existingNames = [];
                static string GetName(ModuleDefinition module, DelegateSignature signature) {
                    var key = signature.ToStableKeyString();
                    if (nameMap.TryGetValue(key, out var name)) {
                        return name;
                    }
                    else {
                        name = null;
                    }
                    if (!signature.ParameterTypes.Any(p => p is TypeSpecification) && signature.ParameterTypes.Length <= 4) {
                        if (signature.ReturnType.FullName == module.TypeSystem.Boolean.FullName) {
                            name = $"Pred{string.Join("", signature.ParameterTypes.Select(p => p.Name))}";
                        }
                        else if (signature.ReturnType.FullName == module.TypeSystem.Void.FullName) {
                            name = $"Action{string.Join("", signature.ParameterTypes.Select(p => p.Name))}";
                        }
                        else {
                            name = $"Func{string.Join("", signature.ParameterTypes.Select(p => p.Name))}";
                        }
                    }
                    if (name is not null && !existingNames.Contains(name)) {
                        nameMap.Add(key, name);
                        existingNames.Add(name);
                        return name;
                    }
                    var stable = signature.ToStableKeyString();
                    var hash = ComputeHexHash(stable, 8);
                    name = "D_" + hash;
                    return name;
                }
                public static GeneratedDelegate GetOrCreateDelegateType(ModuleDefinition module, TypeDefinition container, DelegateSignature signature) {
                    var name = GetName(module, signature);

                    TypeDefinition? existing = container.NestedTypes.FirstOrDefault(t => t.Name == name && t.BaseType?.FullName == typeof(MulticastDelegate).FullName);
                    if (existing is not null) {
                        return new GeneratedDelegate(existing);
                    }

                    var multicastDelegate = new TypeReference("System", nameof(MulticastDelegate), module, module.TypeSystem.CoreLibrary);
                    var newDel = new TypeDefinition(container.Namespace, name,
                        TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.Class,
                        multicastDelegate);

                    container.NestedTypes.Add(newDel);

                    AddDelegateCtor(module, newDel);
                    AddInvoke(module, newDel, signature);
                    AddBeginInvoke(module, newDel, signature);
                    AddEndInvoke(module, newDel, signature);

                    return new GeneratedDelegate(newDel);
                }

                private static void AddDelegateCtor(ModuleDefinition module, TypeDefinition del) {
                    var ctor = new MethodDefinition(".ctor",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                        module.TypeSystem.Void) {
                        HasThis = true,
                        ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                    };
                    ctor.Parameters.Add(new ParameterDefinition("@object", ParameterAttributes.None, module.TypeSystem.Object));
                    ctor.Parameters.Add(new ParameterDefinition("method", ParameterAttributes.None, module.TypeSystem.IntPtr));
                    del.Methods.Add(ctor);
                }

                private static void AddInvoke(ModuleDefinition module, TypeDefinition del, DelegateSignature signature) {
                    var invoke = new MethodDefinition("Invoke",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                        signature.ReturnType) {
                        HasThis = true,
                        ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                    };
                    if (signature.ParameterTypes.Length is 1) {
                        invoke.Parameters.Add(new ParameterDefinition("obj", ParameterAttributes.None, signature.ParameterTypes[0]));
                    }
                    else {
                        for (int i = 0; i < signature.ParameterTypes.Length; i++) {
                            TypeReference p = signature.ParameterTypes[i];
                            invoke.Parameters.Add(new ParameterDefinition("arg" + (i + 1), ParameterAttributes.None, p));
                        }
                    }
                    del.Methods.Add(invoke);
                }

                private static void AddBeginInvoke(ModuleDefinition module, TypeDefinition del, DelegateSignature signature) {
                    var iasyncResult = new TypeReference("System", nameof(IAsyncResult), module, module.TypeSystem.CoreLibrary);
                    var asyncCallback = new TypeReference("System", nameof(AsyncCallback), module, module.TypeSystem.CoreLibrary);

                    var beginInvoke = new MethodDefinition("BeginInvoke",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                        iasyncResult) {
                        HasThis = true,
                        ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                    };

                    if (signature.ParameterTypes.Length is 1) {
                        beginInvoke.Parameters.Add(new ParameterDefinition("obj", ParameterAttributes.None, signature.ParameterTypes[0]));
                    }
                    else {
                        for (int i = 0; i < signature.ParameterTypes.Length; i++) {
                            TypeReference p = signature.ParameterTypes[i];
                            beginInvoke.Parameters.Add(new ParameterDefinition("arg" + (i + 1), ParameterAttributes.None, p));
                        }
                    }
                    beginInvoke.Parameters.Add(new ParameterDefinition("callback", ParameterAttributes.None, asyncCallback));
                    beginInvoke.Parameters.Add(new ParameterDefinition("@object", ParameterAttributes.None, module.TypeSystem.Object));
                    del.Methods.Add(beginInvoke);
                }

                private static void AddEndInvoke(ModuleDefinition module, TypeDefinition del, DelegateSignature signature) {
                    var iasyncResult = new TypeReference("System", nameof(IAsyncResult), module, module.TypeSystem.CoreLibrary);

                    var endInvoke = new MethodDefinition("EndInvoke",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                        signature.ReturnType) {
                        HasThis = true,
                        ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                    };

                    for (int i = 0; i < signature.ParameterTypes.Length; i++) {
                        TypeReference p = signature.ParameterTypes[i];
                        if (p is ByReferenceType) {
                            endInvoke.Parameters.Add(new ParameterDefinition("arg" + (i + 1), ParameterAttributes.None, p));
                        }
                    }
                    endInvoke.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.None, iasyncResult));
                    del.Methods.Add(endInvoke);
                }

                private static string ComputeHexHash(string input, int hexChars) {
                    var bytes = Encoding.UTF8.GetBytes(input);
                    var hash = SHA256.HashData(bytes);
                    var sb = new StringBuilder(hexChars);
                    for (int i = 0; i < hexChars / 2; i++) {
                        sb.Append(hash[i].ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
        }

        sealed class Engine(
            ModuleDefinition module,
            Dictionary<FieldDefinition, List<(MethodDefinition Method, Instruction Inst)>> fieldUseSites,
            Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> callUseSites,
            SignaturePlanner planner,
            Func<MethodDefinition, Dictionary<Instruction, List<Instruction>>> getJumpSites)
        {
            readonly ModuleDefinition module = module ?? throw new ArgumentNullException(nameof(module));
            readonly Dictionary<FieldDefinition, List<(MethodDefinition Method, Instruction Inst)>> fieldUseSites = fieldUseSites ?? throw new ArgumentNullException(nameof(fieldUseSites));
            readonly Dictionary<MethodDefinition, List<(MethodDefinition Caller, Instruction CallInst)>> callUseSites = callUseSites ?? throw new ArgumentNullException(nameof(callUseSites));
            readonly SignaturePlanner planner = planner ?? throw new ArgumentNullException(nameof(planner));
            readonly Func<MethodDefinition, Dictionary<Instruction, List<Instruction>>> getJumpSites = getJumpSites ?? throw new ArgumentNullException(nameof(getJumpSites));

            private sealed record ValueSeed(Instruction Push, TypeReference InfectedType, Tasks.DelegateTransform Transform);
            private sealed record MethodSeedWorkItem(MethodDefinition Method, ValueSeed Seed);

            private bool TryRewriteExternalGenericInstantiation(MethodReference calleeRef, Tasks.DelegateTransform transform) {
                ArgumentNullException.ThrowIfNull(calleeRef);
                ArgumentNullException.ThrowIfNull(transform);

                var anyChanged = false;

                if (calleeRef.DeclaringType is not null) {
                    TypeReference newDecl = transform.TransformType(calleeRef.DeclaringType);
                    if (newDecl.FullName != calleeRef.DeclaringType.FullName) {
                        calleeRef.DeclaringType = newDecl;
                        anyChanged = true;
                    }
                }

                if (calleeRef is GenericInstanceMethod gim) {
                    for (int i = 0; i < gim.GenericArguments.Count; i++) {
                        TypeReference oldArg = gim.GenericArguments[i];
                        TypeReference newArg = transform.TransformType(oldArg);
                        if (newArg.FullName != oldArg.FullName) {
                            gim.GenericArguments[i] = newArg;
                            anyChanged = true;
                        }
                    }
                }

                return anyChanged;
            }

            private bool TryRetargetCallToTransformedDeclaringType(
                Instruction callInst,
                MethodReference calleeRef,
                Tasks.DelegateTransform transform) {

                ArgumentNullException.ThrowIfNull(callInst);
                ArgumentNullException.ThrowIfNull(calleeRef);
                ArgumentNullException.ThrowIfNull(transform);

                TypeReference? oldDecl = calleeRef.DeclaringType;
                if (oldDecl is null) {
                    return false;
                }

                TypeReference newDecl = transform.TransformType(oldDecl);
                if (newDecl.FullName == oldDecl.FullName) {
                    return false;
                }

                TypeDefinition? newDeclDef = newDecl.TryResolve();
                if (newDeclDef is null || newDeclDef.Module != module) {
                    return false;
                }

                MethodDefinition methodDef = newDeclDef.GetMethod(calleeRef.Name);

                // Action<Arg1,Arg2>.Invoke(!T1, !T2) → MyDele.Invoke(Arg1, Arg2)

                callInst.Operand = MonoModCommon.Structure.CreateMethodReference(methodDef, methodDef);
                return true;
            }

            public void Run(Tasks.DelegateTaskGroup[] taskGroups) {
                var globalVisited = new HashSet<(MethodDefinition Method, int PushOffset, string InfectedTypeFullName)>();
                var globalWork = new Stack<MethodSeedWorkItem>();

                var fieldToTransform = new Dictionary<FieldDefinition, Tasks.DelegateTransform>();
                foreach (Tasks.DelegateTaskGroup group in taskGroups) {
                    foreach (FieldDefinition field in group.Fields) {
                        fieldToTransform[field] = group.Transform;
                    }
                }

                // Seed from uses of task fields.
                foreach ((FieldDefinition? field, Tasks.DelegateTransform? transform) in fieldToTransform) {
                    if (!fieldUseSites.TryGetValue(field, out List<(MethodDefinition Method, Instruction Inst)>? useSites)) {
                        continue;
                    }
                    foreach ((MethodDefinition? method, Instruction? inst) in useSites) {
                        if (!method.HasBody) {
                            continue;
                        }
                        if (inst.Operand is not FieldReference fieldRef) {
                            continue;
                        }
                        fieldRef.FieldType = transform.TransformType(fieldRef.FieldType);

                        if (inst.OpCode.Code is Code.Stfld or Code.Stsfld) {
                            SeedExpectedValueIntoFieldStore(method, inst, fieldRef.FieldType, transform, globalWork);
                            continue;
                        }

                        TypeReference infectedType = inst.OpCode.Code is Code.Ldflda or Code.Ldsflda
                            ? new ByReferenceType(fieldRef.FieldType)
                            : fieldRef.FieldType;
                        globalWork.Push(new MethodSeedWorkItem(method, new ValueSeed(inst, infectedType, transform)));
                    }
                }

                while (globalWork.Count > 0) {
                    MethodSeedWorkItem workItem = globalWork.Pop();
                    MethodDefinition method = workItem.Method;
                    if (!method.HasBody) {
                        continue;
                    }

                    (MethodDefinition Method, int PushOffset, string InfectedTypeFullName) visitedKey = (Method: method, PushOffset: workItem.Seed.Push.Offset, InfectedTypeFullName: workItem.Seed.InfectedType.FullName);
                    if (!globalVisited.Add(visitedKey)) {
                        continue;
                    }

                    ProcessMethodSeed(method, workItem.Seed, globalWork);
                }
            }

            private void ProcessMethodSeed(MethodDefinition method, ValueSeed seed, Stack<MethodSeedWorkItem> globalWork) {
                Dictionary<Instruction, List<Instruction>> jumpSites = getJumpSites(method);
                BuildMethodIndex(method, out Dictionary<int, List<Instruction>>? ldlocSites, out Dictionary<int, List<Instruction>>? ldlocaSites, out Dictionary<int, List<Instruction>>? ldargSites, out Dictionary<int, List<Instruction>>? ldargaSites);

                var localVisited = new HashSet<(int PushOffset, string InfectedTypeFullName)>();
                var valueWork = new Stack<ValueSeed>();
                valueWork.Push(seed);

                while (valueWork.Count > 0) {
                    ValueSeed current = valueWork.Pop();
                    if (!localVisited.Add((current.Push.Offset, current.InfectedType.FullName))) {
                        continue;
                    }

                    HandleValueProducer(method, current, globalWork);
                    TryPlanParameterChangeFromLoad(method, current, globalWork);

                    foreach (Instruction consumer in MonoModCommon.Stack.TraceStackValueConsumers(method, current.Push)) {
                        if (current.InfectedType is ByReferenceType byRef && IsIndirectLoad(consumer.OpCode.Code)) {
                            valueWork.Push(new ValueSeed(consumer, byRef.ElementType, current.Transform));
                            continue;
                        }

                        if (consumer.OpCode.Code is Code.Castclass or Code.Isinst) {
                            if (consumer.Operand is TypeReference tr) {
                                TypeReference transformed = current.Transform.TransformType(tr);
                                if (transformed.FullName != tr.FullName) {
                                    consumer.Operand = transformed;
                                }

                                if (TypeContainsDelegate(transformed, current.Transform.NewDelegateTypeDef.FullName)) {
                                    valueWork.Push(new ValueSeed(consumer, transformed, current.Transform));
                                }
                            }
                            continue;
                        }

                        if (consumer.OpCode.Code is Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3 or Code.Stloc_S or Code.Stloc) {
                            HandleStoreLocal(method, consumer, current, ldlocSites, ldlocaSites, valueWork);
                            continue;
                        }

                        if (consumer.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj) {
                            HandleCallConsumer(method, consumer, current, jumpSites, ldlocSites, ldlocaSites, ldargSites, ldargaSites, valueWork, globalWork);
                            continue;
                        }

                        if (consumer.OpCode.Code is Code.Stfld or Code.Stsfld) {
                            HandleStoreField(consumer, current, globalWork);
                            continue;
                        }

                        if (consumer.OpCode.Code is Code.Ret) {
                            if (method.ReturnType.FullName == module.TypeSystem.Void.FullName) {
                                continue;
                            }

                            TypeReference expectedReturnType = current.Transform.TransformType(method.ReturnType);
                            if (expectedReturnType.FullName != current.InfectedType.FullName) {
                                continue;
                            }

                            if (expectedReturnType.FullName != method.ReturnType.FullName) {
                                PlanReturnTypeChangeAndEnqueueCallersAndSeedReturns(method, expectedReturnType, current.Transform, globalWork);
                            }
                            continue;
                        }
                    }
                }
            }

            private void HandleValueProducer(MethodDefinition method, ValueSeed infected, Stack<MethodSeedWorkItem> globalWork) {
                if (!method.HasBody) {
                    return;
                }

                if (infected.Push.OpCode.Code is Code.Newobj
                    && infected.Push.Operand is MethodReference ctorRef
                    && ctorRef.Name is ".ctor"
                    && ctorRef.DeclaringType is not null
                    && infected.InfectedType is not ByReferenceType) {

                    TypeReference oldDecl = ctorRef.DeclaringType;
                    TypeReference newDecl = infected.Transform.TransformType(oldDecl);
                    if (newDecl.FullName != oldDecl.FullName && newDecl.FullName == infected.InfectedType.FullName) {
                        TypeDefinition? newDeclDef = newDecl.TryResolve();
                        if (newDeclDef is not null && newDeclDef.Module == module) {
                            MethodDefinition newCtorDef = newDeclDef.GetMethod(".ctor");
                            infected.Push.Operand = MonoModCommon.Structure.CreateMethodReference(newCtorDef, newCtorDef);
                        }
                    }
                }

                if (infected.Push.OpCode.Code is Code.Call or Code.Callvirt
                    && infected.Push.Operand is MethodReference calleeRef) {

                    MethodReference typed = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);
                    TypeReference formalReturn = typed.ReturnType;
                    TypeReference expectedReturn = infected.Transform.TransformType(formalReturn);
                    if (expectedReturn.FullName != infected.InfectedType.FullName) {
                        return;
                    }
                    if (expectedReturn.FullName == formalReturn.FullName) {
                        return;
                    }

                    MethodDefinition? calleeDef = calleeRef.TryResolve();
                    if (calleeDef is null || calleeDef.Module != module) {
                        if (TryRewriteExternalGenericInstantiation(calleeRef, infected.Transform)) {
                            MethodReference typedAfter = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);
                            if (typedAfter.ReturnType.FullName == expectedReturn.FullName) {
                                return;
                            }
                        }
                        throw new NotSupportedException($"External method '{calleeRef.FullName}' requires return type changes for '{formalReturn.FullName}' -> '{expectedReturn.FullName}'.");
                    }

                    PlanReturnTypeChangeAndEnqueueCallersAndSeedReturns(calleeDef, expectedReturn, infected.Transform, globalWork);
                }
            }

            private static void BuildMethodIndex(
                MethodDefinition method,
                out Dictionary<int, List<Instruction>> ldlocSites,
                out Dictionary<int, List<Instruction>> ldlocaSites,
                out Dictionary<int, List<Instruction>> ldargSites,
                out Dictionary<int, List<Instruction>> ldargaSites) {

                ldlocSites = [];
                ldlocaSites = [];
                ldargSites = [];
                ldargaSites = [];

                foreach (Instruction? inst in method.Body.Instructions) {
                    if (MonoModCommon.IL.TryGetReferencedVariable(method, inst, out var localIndex, out _)
                        && inst.OpCode.Code is Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S or Code.Ldloc) {
                        if (!ldlocSites.TryGetValue(localIndex, out List<Instruction>? list)) {
                            ldlocSites.Add(localIndex, list = []);
                        }
                        list.Add(inst);
                        continue;
                    }

                    if (MonoModCommon.IL.TryGetReferencedVariable(method, inst, out localIndex, out _)
                        && inst.OpCode.Code is Code.Ldloca or Code.Ldloca_S) {
                        if (!ldlocaSites.TryGetValue(localIndex, out List<Instruction>? list)) {
                            ldlocaSites.Add(localIndex, list = []);
                        }
                        list.Add(inst);
                        continue;
                    }

                    if (MonoModCommon.IL.TryGetReferencedParameter(method, inst, out var innerIndex, out _)
                        && inst.OpCode.Code is Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S or Code.Ldarg) {
                        if (!ldargSites.TryGetValue(innerIndex, out List<Instruction>? list)) {
                            ldargSites.Add(innerIndex, list = []);
                        }
                        list.Add(inst);
                        continue;
                    }

                    if (MonoModCommon.IL.TryGetReferencedParameter(method, inst, out innerIndex, out _)
                        && inst.OpCode.Code is Code.Ldarga or Code.Ldarga_S) {
                        if (!ldargaSites.TryGetValue(innerIndex, out List<Instruction>? list)) {
                            ldargaSites.Add(innerIndex, list = []);
                        }
                        list.Add(inst);
                        continue;
                    }
                }
            }

            private void TryPlanParameterChangeFromLoad(MethodDefinition method, ValueSeed seed, Stack<MethodSeedWorkItem> globalWork) {
                if (!MonoModCommon.IL.TryGetReferencedParameter(method, seed.Push, out var innerIndex, out ParameterDefinition? parameter)) {
                    return;
                }
                if (innerIndex == 0 && method.HasThis) {
                    return;
                }

                var paramIndex = innerIndex - (method.HasThis ? 1 : 0);
                if (paramIndex < 0 || paramIndex >= method.Parameters.Count) {
                    return;
                }

                TypeReference expected = seed.Transform.TransformType(parameter.ParameterType);
                if (expected.FullName != seed.InfectedType.FullName) {
                    return;
                }

                PlanParameterChangeAndEnqueueCallers(method, paramIndex, expected, seed.Transform, globalWork);
            }

            private void RewriteCallOperandsForSignatureChange(MethodDefinition callee) {
                if (!callUseSites.TryGetValue(callee, out List<(MethodDefinition Caller, Instruction CallInst)>? callers)) {
                    return;
                }

                foreach ((MethodDefinition _, Instruction? callInst) in callers) {
                    if (callInst.Operand is not MethodReference callRef) {
                        continue;
                    }

                    callInst.Operand = MonoModCommon.Structure.CreateMethodReference(callRef, callee);
                }
            }

            private void PlanParameterChangeAndEnqueueCallers(
                MethodDefinition method,
                int paramIndex,
                TypeReference newParameterType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                MethodDefinition[] affected = planner.PlanParameterTypeChange(method, paramIndex, newParameterType);
                foreach (MethodDefinition affectedMethod in affected) {
                    RewriteCallOperandsForSignatureChange(affectedMethod);
                    EnqueueCallersForParamChange(affectedMethod, paramIndex, newParameterType, transform, globalWork);
                }
            }

            private void PlanReturnTypeChangeAndEnqueueCallersAndSeedReturns(
                MethodDefinition method,
                TypeReference newReturnType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                if (method.ReturnType.FullName == newReturnType.FullName) {
                    return;
                }
                if (method.ReturnType.FullName == module.TypeSystem.Void.FullName || newReturnType.FullName == module.TypeSystem.Void.FullName) {
                    throw new NotSupportedException($"Return type changes involving void are not supported for '{method.GetIdentifier()}': '{method.ReturnType.FullName}' -> '{newReturnType.FullName}'.");
                }

                MethodDefinition[] affected = planner.PlanReturnTypeChange(method, newReturnType);
                foreach (MethodDefinition affectedMethod in affected) {
                    RewriteCallOperandsForSignatureChange(affectedMethod);
                    EnqueueCallersForReturnChange(affectedMethod, newReturnType, transform, globalWork);
                    SeedExpectedValueIntoAllReturns(affectedMethod, newReturnType, transform, globalWork);
                }
            }

            private void EnqueueCallersForReturnChange(
                MethodDefinition callee,
                TypeReference expectedReturnType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                if (!callUseSites.TryGetValue(callee, out List<(MethodDefinition Caller, Instruction CallInst)>? callers)) {
                    return;
                }

                foreach ((MethodDefinition? caller, Instruction? callInst) in callers) {
                    if (!caller.HasBody) {
                        continue;
                    }
                    if (callInst.OpCode.Code is Code.Newobj) {
                        continue;
                    }

                    globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(callInst, expectedReturnType, transform)));
                }
            }

            private void SeedExpectedValueIntoAllReturns(
                MethodDefinition method,
                TypeReference expectedReturnType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                if (!method.HasBody) {
                    return;
                }
                if (method.ReturnType.FullName == method.Module.TypeSystem.Void.FullName) {
                    return;
                }

                Dictionary<Instruction, List<Instruction>> jumpSites = getJumpSites(method);
                foreach (Instruction? ret in method.Body.Instructions) {
                    if (ret.OpCode.Code is not Code.Ret) {
                        continue;
                    }

                    foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, ret, jumpSites)) {
                        MonoModCommon.Stack.InstructionArgsSource? valueSource = path.ParametersSources.SingleOrDefault(s => s.Index == 0);
                        if (valueSource is null || valueSource.Instructions.Length == 0) {
                            continue;
                        }

                        Instruction last = valueSource.Instructions.Last();
                        foreach (MonoModCommon.Stack.StackTopTypePath top in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, last, jumpSites)) {
                            globalWork.Push(new MethodSeedWorkItem(method, new ValueSeed(top.RealPushValueInstruction, expectedReturnType, transform)));
                        }
                    }
                }
            }

            private static bool IsIndirectLoad(Code code) => code is
                Code.Ldind_I or
                Code.Ldind_I1 or
                Code.Ldind_I2 or
                Code.Ldind_I4 or
                Code.Ldind_I8 or
                Code.Ldind_R4 or
                Code.Ldind_R8 or
                Code.Ldind_Ref or
                Code.Ldind_U1 or
                Code.Ldind_U2 or
                Code.Ldind_U4 or
                Code.Ldobj;

            private static void HandleStoreLocal(
                MethodDefinition method,
                Instruction stloc,
                ValueSeed stored,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Stack<ValueSeed> valueWork) {

                VariableDefinition variable = MonoModCommon.IL.GetReferencedVariable(method, stloc);
                TypeReference newVarType = stored.Transform.TransformType(variable.VariableType);
                if (newVarType.FullName != variable.VariableType.FullName) {
                    variable.VariableType = newVarType;
                }

                if (ldlocSites.TryGetValue(variable.Index, out List<Instruction>? loads)) {
                    foreach (Instruction ldloc in loads) {
                        valueWork.Push(new ValueSeed(ldloc, stored.InfectedType, stored.Transform));
                    }
                }

                if (ldlocaSites.TryGetValue(variable.Index, out List<Instruction>? addrLoads)) {
                    TypeReference byRef = stored.Transform.TransformType(new ByReferenceType(variable.VariableType));
                    foreach (Instruction ldloca in addrLoads) {
                        valueWork.Push(new ValueSeed(ldloca, byRef, stored.Transform));
                    }
                }
            }

            private delegate bool SpecialCallRule(
                Engine engine,
                MethodDefinition caller,
                Instruction callInst,
                MethodReference calleeRef,
                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path,
                IReadOnlyList<int> infectedArgIndices,
                ValueSeed infected,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork);

            private static readonly SpecialCallRule[] SpecialCallRules = [
                TryHandleSystemDelegateCombineOrRemove,
            ];

            private bool TryApplySpecialCallRulesForPath(
                MethodDefinition caller,
                Instruction callInst,
                MethodReference calleeRef,
                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path,
                IReadOnlyList<int> infectedArgIndices,
                ValueSeed infected,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork) {

                foreach (SpecialCallRule rule in SpecialCallRules) {
                    if (rule(
                        this,
                        caller,
                        callInst,
                        calleeRef,
                        path,
                        infectedArgIndices,
                        infected,
                        jumpSites,
                        ldlocSites,
                        ldlocaSites,
                        ldargSites,
                        ldargaSites,
                        valueWork,
                        globalWork)) {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryHandleSystemDelegateCombineOrRemove(
                Engine engine,
                MethodDefinition caller,
                Instruction callInst,
                MethodReference calleeRef,
                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path,
                IReadOnlyList<int> infectedArgIndices,
                ValueSeed infected,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork) {

                if (calleeRef.DeclaringType?.FullName is not "System.Delegate") {
                    return false;
                }

                if (calleeRef.Name is not ("Combine" or "Remove")) {
                    return false;
                }

                if (calleeRef.Parameters.Count != 2) {
                    return false;
                }

                if (path.ParametersSources.Length != 2) {
                    return false;
                }

                TypeReference expected = UnwrapByRef(infected.InfectedType);
                if (expected.FullName is "System.Delegate" or "System.MulticastDelegate" or "System.Object") {
                    return false;
                }

                for (int argIndex = 0; argIndex < path.ParametersSources.Length; argIndex++) {
                    MonoModCommon.Stack.ParameterSource source = path.ParametersSources[argIndex];
                    if (source.Instructions.Length == 0) {
                        continue;
                    }
                    engine.PropagateExpectedTypeIntoArgumentSource(
                        caller,
                        source.Instructions.Last(),
                        expected,
                        infected.Transform,
                        jumpSites,
                        ldlocSites,
                        ldlocaSites,
                        ldargSites,
                        ldargaSites,
                        valueWork,
                        globalWork);
                }

                if (MonoModCommon.Stack.GetPushCount(caller.Body, callInst) > 0) {
                    valueWork.Push(new ValueSeed(callInst, expected, infected.Transform));
                }

                return true;
            }

            private void HandleCallConsumer(
                MethodDefinition caller,
                Instruction callInst,
                ValueSeed infected,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork) {

                var calleeRef = (MethodReference)callInst.Operand;
                MethodDefinition? calleeDef = calleeRef.TryResolve();

                var includeThis = (callInst.OpCode == OpCodes.Call || callInst.OpCode == OpCodes.Callvirt) && calleeRef.HasThis;
                var thisOffset = includeThis ? 1 : 0;

                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paths = MonoModCommon.Stack.AnalyzeParametersSources(caller, callInst, jumpSites);
                foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path in paths) {
                    IReadOnlyList<int> infectedArgIndices = FindInfectedArgIndices(path, infected.Push);
                    if (infectedArgIndices.Count == 0) {
                        continue;
                    }

                    if (TryApplySpecialCallRulesForPath(
                        caller,
                        callInst,
                        calleeRef,
                        path,
                        infectedArgIndices,
                        infected,
                        jumpSites,
                        ldlocSites,
                        ldlocaSites,
                        ldargSites,
                        ldargaSites,
                        valueWork,
                        globalWork)) {
                        continue;
                    }

                    foreach (var argIndex in infectedArgIndices) {
                        if (includeThis && argIndex == 0) {
                            PropagateFromThisInfected(caller, callInst, calleeRef, calleeDef, path, infected, jumpSites, ldlocSites, ldlocaSites, ldargSites, ldargaSites, valueWork, globalWork);
                            continue;
                        }

                        var paramIndex = argIndex - thisOffset;
                        if (paramIndex < 0 || paramIndex >= calleeRef.Parameters.Count) {
                            continue;
                        }

                        TypeReference formalUntyped = UnwrapByRef(calleeRef.Parameters[paramIndex].ParameterType);
                        if (formalUntyped is GenericParameter gp && includeThis) {
                            if (TryInferNewDeclaringTypeFromDeclaringGenericParam(calleeRef.DeclaringType, gp, infected.InfectedType, out TypeReference? newDeclType)) {
                                if (newDeclType.FullName != calleeRef.DeclaringType.FullName) {
                                    calleeRef.DeclaringType = newDeclType;

                                    MonoModCommon.Stack.ParameterSource thisSource = path.ParametersSources[0];
                                    foreach (MonoModCommon.Stack.StackTopTypePath thisTop in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, thisSource.Instructions.Last(), jumpSites)) {
                                        valueWork.Push(new ValueSeed(thisTop.RealPushValueInstruction, newDeclType, infected.Transform));
                                    }
                                }
                            }
                            continue;
                        }

                        MethodReference typed = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);
                        TypeReference formalTyped = typed.Parameters[paramIndex].ParameterType;
                        TypeReference expected = infected.Transform.TransformType(formalTyped);
                        if (expected.FullName == formalTyped.FullName) {
                            continue;
                        }

                        if (calleeDef is null || calleeDef.Module != module) {
                            if (TryRewriteExternalGenericInstantiation(calleeRef, infected.Transform)) {
                                MethodReference typedAfter = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);
                                TypeReference formalAfter = typedAfter.Parameters[paramIndex].ParameterType;
                                if (formalAfter.FullName == expected.FullName) {
                                    continue;
                                }
                            }
                            throw new NotSupportedException($"External method '{calleeRef.FullName}' requires signature changes for '{formalTyped.FullName}' -> '{expected.FullName}'.");
                        }

                        PlanParameterChangeAndEnqueueCallers(calleeDef, paramIndex, expected, infected.Transform, globalWork);
                    }
                }
            }

            private void PropagateFromThisInfected(
                MethodDefinition caller,
                Instruction callInst,
                MethodReference calleeRef,
                MethodDefinition? calleeDef,
                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path,
                ValueSeed infectedThis,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork) {

                if (calleeDef is null || calleeDef.Module != module) {
                    if (TryRetargetCallToTransformedDeclaringType(callInst, calleeRef, infectedThis.Transform)) {
                        calleeRef = (MethodReference)callInst.Operand;
                        calleeDef = calleeRef.TryResolve();
                    }
                }

                if (calleeRef.DeclaringType is GenericInstanceType oldDecl && infectedThis.InfectedType is GenericInstanceType newDecl) {
                    if (oldDecl.ElementType.FullName == newDecl.ElementType.FullName) {
                        calleeRef.DeclaringType = newDecl;
                    }
                }

                MethodReference typed = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);

                if (calleeDef is null || calleeDef.Module != module) {
                    if (TryRewriteExternalGenericInstantiation(calleeRef, infectedThis.Transform)) {
                        typed = MonoModCommon.Structure.CreateInstantiatedMethod(calleeRef);
                    }
                    foreach (ParameterDefinition? p in typed.Parameters) {
                        TypeReference transformed = infectedThis.Transform.TransformType(p.ParameterType);
                        if (transformed.FullName != p.ParameterType.FullName) {
                            throw new NotSupportedException($"External method '{calleeRef.FullName}' requires signature changes for '{p.ParameterType.FullName}' -> '{transformed.FullName}'.");
                        }
                    }
                }
                else {
                    for (int i = 0; i < calleeDef.Parameters.Count; i++) {
                        TypeReference transformed = infectedThis.Transform.TransformType(calleeDef.Parameters[i].ParameterType);
                        if (transformed.FullName != calleeDef.Parameters[i].ParameterType.FullName) {
                            PlanParameterChangeAndEnqueueCallers(calleeDef, i, transformed, infectedThis.Transform, globalWork);
                        }
                    }
                }

                for (int argIndex = 0; argIndex < path.ParametersSources.Length; argIndex++) {
                    if (argIndex == 0) {
                        continue;
                    }

                    var paramIndex = argIndex - 1;
                    if (paramIndex < 0 || paramIndex >= typed.Parameters.Count) {
                        continue;
                    }

                    TypeReference expected = typed.Parameters[paramIndex].ParameterType;
                    if (!TypeContainsDelegate(expected, infectedThis.Transform.NewDelegateTypeDef.FullName)) {
                        continue;
                    }

                    MonoModCommon.Stack.ParameterSource source = path.ParametersSources[argIndex];
                    PropagateExpectedTypeIntoArgumentSource(caller, source.Instructions.Last(), expected, infectedThis.Transform, jumpSites, ldlocSites, ldlocaSites, ldargSites, ldargaSites, valueWork, globalWork);
                }

                if (MonoModCommon.Stack.GetPushCount(caller.Body, callInst) > 0) {
                    TypeReference expectedReturn = typed.ReturnType;
                    if (TypeContainsDelegate(expectedReturn, infectedThis.Transform.NewDelegateTypeDef.FullName)) {
                        valueWork.Push(new ValueSeed(callInst, expectedReturn, infectedThis.Transform));
                    }
                }
            }

            private void PropagateExpectedTypeIntoArgumentSource(
                MethodDefinition caller,
                Instruction lastArgInstruction,
                TypeReference expectedParamType,
                Tasks.DelegateTransform transform,
                Dictionary<Instruction, List<Instruction>> jumpSites,
                Dictionary<int, List<Instruction>> ldlocSites,
                Dictionary<int, List<Instruction>> ldlocaSites,
                Dictionary<int, List<Instruction>> ldargSites,
                Dictionary<int, List<Instruction>> ldargaSites,
                Stack<ValueSeed> valueWork,
                Stack<MethodSeedWorkItem> globalWork) {

                if (expectedParamType is ByReferenceType byRef) {
                    TypeReference expectedElement = byRef.ElementType;

                    if (lastArgInstruction.OpCode.Code is Code.Ldloca or Code.Ldloca_S
                        && MonoModCommon.IL.TryGetReferencedVariable(caller, lastArgInstruction, out var localIndex, out VariableDefinition? variable)) {
                        if (variable.VariableType.FullName != expectedElement.FullName) {
                            variable.VariableType = expectedElement;
                        }
                        if (ldlocSites.TryGetValue(localIndex, out List<Instruction>? loads)) {
                            foreach (Instruction load in loads) {
                                valueWork.Push(new ValueSeed(load, expectedElement, transform));
                            }
                        }
                        if (ldlocaSites.TryGetValue(localIndex, out List<Instruction>? addrLoads)) {
                            foreach (Instruction load in addrLoads) {
                                valueWork.Push(new ValueSeed(load, expectedParamType, transform));
                            }
                        }
                        valueWork.Push(new ValueSeed(lastArgInstruction, expectedParamType, transform));
                        return;
                    }

                    if (lastArgInstruction.OpCode.Code is Code.Ldarga or Code.Ldarga_S
                        && MonoModCommon.IL.TryGetReferencedParameter(caller, lastArgInstruction, out var innerIndex, out ParameterDefinition? parameter)) {
                        if (innerIndex != 0 || !caller.HasThis) {
                            var paramIndex = innerIndex - (caller.HasThis ? 1 : 0);
                            if (paramIndex >= 0 && paramIndex < caller.Parameters.Count) {
                                if (parameter.ParameterType.FullName != expectedElement.FullName) {
                                    PlanParameterChangeAndEnqueueCallers(caller, paramIndex, expectedElement, transform, globalWork);
                                }
                                if (ldargSites.TryGetValue(innerIndex, out List<Instruction>? loads)) {
                                    foreach (Instruction load in loads) {
                                        valueWork.Push(new ValueSeed(load, expectedElement, transform));
                                    }
                                }
                                if (ldargaSites.TryGetValue(innerIndex, out List<Instruction>? addrLoads)) {
                                    foreach (Instruction load in addrLoads) {
                                        valueWork.Push(new ValueSeed(load, expectedParamType, transform));
                                    }
                                }
                            }
                        }
                        valueWork.Push(new ValueSeed(lastArgInstruction, expectedParamType, transform));
                        return;
                    }

                    if (lastArgInstruction.OpCode.Code is Code.Ldflda or Code.Ldsflda
                        && lastArgInstruction.Operand is FieldReference fr) {
                        FieldDefinition? fieldDef = fr.TryResolve();
                        if (fieldDef is not null && fieldDef.Module == module) {
                            if (fieldDef.FieldType.FullName != expectedElement.FullName) {
                                fieldDef.FieldType = expectedElement;
                            }
                            fr.FieldType = expectedElement;

                            if (fieldUseSites.TryGetValue(fieldDef, out List<(MethodDefinition Method, Instruction Inst)>? useSites)) {
                                foreach ((MethodDefinition? useMethod, Instruction? inst) in useSites) {
                                    if (!useMethod.HasBody) {
                                        continue;
                                    }
                                    if (inst.OpCode.Code is Code.Ldfld or Code.Ldsfld) {
                                        var operand = (FieldReference)inst.Operand;
                                        operand.FieldType = expectedElement;
                                        valueWork.Push(new ValueSeed(inst, expectedElement, transform));
                                    }
                                    else if (inst.OpCode.Code is Code.Ldflda or Code.Ldsflda) {
                                        var operand = (FieldReference)inst.Operand;
                                        operand.FieldType = expectedElement;
                                        valueWork.Push(new ValueSeed(inst, expectedParamType, transform));
                                    }
                                }
                            }
                        }

                        valueWork.Push(new ValueSeed(lastArgInstruction, expectedParamType, transform));
                        return;
                    }

                    throw new NotSupportedException($"Unsupported byref argument source in '{caller.GetIdentifier()}': '{lastArgInstruction.OpCode.Code}'.");
                }

                foreach (MonoModCommon.Stack.StackTopTypePath top in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, lastArgInstruction, jumpSites)) {
                    valueWork.Push(new ValueSeed(top.RealPushValueInstruction, expectedParamType, transform));
                }
            }

            private void HandleStoreField(Instruction stfld, ValueSeed stored, Stack<MethodSeedWorkItem> globalWork) {
                var fieldRef = (FieldReference)stfld.Operand;
                FieldDefinition? fieldDef = fieldRef.TryResolve();
                if (fieldDef is null || fieldDef.Module != module) {
                    return;
                }

                TypeReference transformed = stored.Transform.TransformType(fieldDef.FieldType);
                if (transformed.FullName == fieldDef.FieldType.FullName) {
                    return;
                }

                fieldDef.FieldType = transformed;
                fieldRef.FieldType = transformed;

                if (!fieldUseSites.TryGetValue(fieldDef, out List<(MethodDefinition Method, Instruction Inst)>? useSites)) {
                    return;
                }

                foreach ((MethodDefinition? useMethod, Instruction? inst) in useSites) {
                    if (!useMethod.HasBody) {
                        continue;
                    }
                    if (inst.OpCode.Code is Code.Ldfld or Code.Ldsfld) {
                        var operand = (FieldReference)inst.Operand;
                        operand.FieldType = transformed;
                        globalWork.Push(new MethodSeedWorkItem(useMethod, new ValueSeed(inst, transformed, stored.Transform)));
                    }
                    else if (inst.OpCode.Code is Code.Ldflda or Code.Ldsflda) {
                        var operand = (FieldReference)inst.Operand;
                        operand.FieldType = transformed;
                        globalWork.Push(new MethodSeedWorkItem(useMethod, new ValueSeed(inst, new ByReferenceType(transformed), stored.Transform)));
                    }
                    else if (inst.OpCode.Code is Code.Stfld or Code.Stsfld) {
                        var operand = (FieldReference)inst.Operand;
                        operand.FieldType = transformed;
                        SeedExpectedValueIntoFieldStore(useMethod, inst, transformed, stored.Transform, globalWork);
                    }
                }
            }

            private void SeedExpectedValueIntoFieldStore(
                MethodDefinition method,
                Instruction stfldOrStsfld,
                TypeReference expectedValueType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                if (!method.HasBody) {
                    return;
                }

                Dictionary<Instruction, List<Instruction>> jumpSites = getJumpSites(method);
                foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.InstructionArgsSource> path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(method, stfldOrStsfld, jumpSites)) {
                    MonoModCommon.Stack.InstructionArgsSource? valueSource = path.ParametersSources.SingleOrDefault(s => s.Index == 0);
                    if (valueSource is null || valueSource.Instructions.Length == 0) {
                        continue;
                    }
                    Instruction last = valueSource.Instructions.Last();
                    foreach (MonoModCommon.Stack.StackTopTypePath top in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(method, last, jumpSites)) {
                        globalWork.Push(new MethodSeedWorkItem(method, new ValueSeed(top.RealPushValueInstruction, expectedValueType, transform)));
                    }
                }
            }

            private void EnqueueCallersForParamChange(
                MethodDefinition callee,
                int paramIndex,
                TypeReference expectedParamType,
                Tasks.DelegateTransform transform,
                Stack<MethodSeedWorkItem> globalWork) {

                if (!callUseSites.TryGetValue(callee, out List<(MethodDefinition Caller, Instruction CallInst)>? callers)) {
                    return;
                }

                foreach ((MethodDefinition? caller, Instruction? callInst) in callers) {
                    if (!caller.HasBody) {
                        continue;
                    }
                    if (callInst.Operand is not MethodReference callRef) {
                        continue;
                    }

                    var includeThis = (callInst.OpCode == OpCodes.Call || callInst.OpCode == OpCodes.Callvirt) && callRef.HasThis;
                    var argIndex = paramIndex + (includeThis ? 1 : 0);

                    Dictionary<Instruction, List<Instruction>> jumpSites = getJumpSites(caller);
                    MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource>[] paths = MonoModCommon.Stack.AnalyzeParametersSources(caller, callInst, jumpSites);
                    foreach (MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path in paths) {
                        if (argIndex < 0 || argIndex >= path.ParametersSources.Length) {
                            continue;
                        }
                        Instruction srcLast = path.ParametersSources[argIndex].Instructions.Last();

                        if (expectedParamType is ByReferenceType byRef) {
                            TypeReference element = byRef.ElementType;

                            if (srcLast.OpCode.Code is Code.Ldloca or Code.Ldloca_S
                                && MonoModCommon.IL.TryGetReferencedVariable(caller, srcLast, out var localIndex, out VariableDefinition? variable)) {
                                if (variable.VariableType.FullName != element.FullName) {
                                    variable.VariableType = element;
                                }
                                foreach (Instruction? inst in caller.Body.Instructions) {
                                    if (MonoModCommon.IL.TryGetReferencedVariable(caller, inst, out var idx, out _)
                                        && idx == localIndex
                                        && inst.OpCode.Code is Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S or Code.Ldloc) {
                                        globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(inst, element, transform)));
                                    }
                                    else if (MonoModCommon.IL.TryGetReferencedVariable(caller, inst, out idx, out _)
                                        && idx == localIndex
                                        && inst.OpCode.Code is Code.Ldloca or Code.Ldloca_S) {
                                        globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(inst, expectedParamType, transform)));
                                    }
                                }
                                globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(srcLast, expectedParamType, transform)));
                                continue;
                            }

                            if (srcLast.OpCode.Code is Code.Ldarga or Code.Ldarga_S
                                && MonoModCommon.IL.TryGetReferencedParameter(caller, srcLast, out var innerIndex, out ParameterDefinition? parameter)) {
                                if (innerIndex != 0 || !caller.HasThis) {
                                    var callerParamIndex = innerIndex - (caller.HasThis ? 1 : 0);
                                    if (callerParamIndex >= 0 && callerParamIndex < caller.Parameters.Count) {
                                        if (parameter.ParameterType.FullName != element.FullName) {
                                            PlanParameterChangeAndEnqueueCallers(caller, callerParamIndex, element, transform, globalWork);
                                        }
                                        foreach (Instruction? inst in caller.Body.Instructions) {
                                            if (MonoModCommon.IL.TryGetReferencedParameter(caller, inst, out var ii, out _)
                                                && ii == innerIndex
                                                && inst.OpCode.Code is Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S or Code.Ldarg) {
                                                globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(inst, element, transform)));
                                            }
                                            else if (MonoModCommon.IL.TryGetReferencedParameter(caller, inst, out ii, out _)
                                                && ii == innerIndex
                                                && inst.OpCode.Code is Code.Ldarga or Code.Ldarga_S) {
                                                globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(inst, expectedParamType, transform)));
                                            }
                                        }
                                    }
                                }
                                globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(srcLast, expectedParamType, transform)));
                                continue;
                            }

                            if (srcLast.OpCode.Code is Code.Ldflda or Code.Ldsflda
                                && srcLast.Operand is FieldReference fr) {
                                FieldDefinition? fieldDef = fr.TryResolve();
                                if (fieldDef is not null && fieldDef.Module == module) {
                                    if (fieldDef.FieldType.FullName != element.FullName) {
                                        fieldDef.FieldType = element;
                                    }
                                    fr.FieldType = element;

                                    if (fieldUseSites.TryGetValue(fieldDef, out List<(MethodDefinition Method, Instruction Inst)>? useSites)) {
                                        foreach ((MethodDefinition? useMethod, Instruction? inst) in useSites) {
                                            if (!useMethod.HasBody) {
                                                continue;
                                            }
                                            if (inst.OpCode.Code is Code.Ldfld or Code.Ldsfld) {
                                                var operand = (FieldReference)inst.Operand;
                                                operand.FieldType = element;
                                                globalWork.Push(new MethodSeedWorkItem(useMethod, new ValueSeed(inst, element, transform)));
                                            }
                                            else if (inst.OpCode.Code is Code.Ldflda or Code.Ldsflda) {
                                                var operand = (FieldReference)inst.Operand;
                                                operand.FieldType = element;
                                                globalWork.Push(new MethodSeedWorkItem(useMethod, new ValueSeed(inst, expectedParamType, transform)));
                                            }
                                        }
                                    }
                                }
                                globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(srcLast, expectedParamType, transform)));
                                continue;
                            }

                            throw new NotSupportedException($"Unsupported byref call argument source in '{caller.GetIdentifier()}': '{srcLast.OpCode.Code}'.");
                        }

                        foreach (MonoModCommon.Stack.StackTopTypePath top in MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(caller, srcLast, jumpSites)) {
                            globalWork.Push(new MethodSeedWorkItem(caller, new ValueSeed(top.RealPushValueInstruction, expectedParamType, transform)));
                        }
                    }
                }
            }

            private static IReadOnlyList<int> FindInfectedArgIndices(
                MonoModCommon.Stack.FlowPath<MonoModCommon.Stack.ParameterSource> path,
                Instruction infectedPush) {

                List<int> indices = [];
                for (int i = 0; i < path.ParametersSources.Length; i++) {
                    if (path.ParametersSources[i].Instructions.Contains(infectedPush)) {
                        indices.Add(i);
                    }
                }
                return indices;
            }

            private static TypeReference UnwrapByRef(TypeReference type) => type is ByReferenceType byRef ? byRef.ElementType : type;

            private static bool TryInferNewDeclaringTypeFromDeclaringGenericParam(
                TypeReference declaringType,
                GenericParameter genericParam,
                TypeReference infectedArgType,
                out TypeReference newDeclaringType) {

                newDeclaringType = declaringType;

                if (declaringType is not GenericInstanceType git) {
                    return false;
                }

                if (genericParam.Owner is not TypeReference ownerType) {
                    return false;
                }

                if (ownerType.TryResolve() is not { } ownerTypeDef) {
                    return false;
                }

                if (git.ElementType.TryResolve() is not { } elementDef) {
                    return false;
                }

                if (ownerTypeDef.FullName != elementDef.FullName) {
                    return false;
                }

                if (genericParam.Position < 0 || genericParam.Position >= git.GenericArguments.Count) {
                    return false;
                }

                var newGit = new GenericInstanceType(git.ElementType);
                for (int i = 0; i < git.GenericArguments.Count; i++) {
                    newGit.GenericArguments.Add(i == genericParam.Position ? infectedArgType : git.GenericArguments[i]);
                }
                newDeclaringType = newGit;
                return true;
            }

            private static bool TypeContainsDelegate(TypeReference type, string delegateFullName) {
                if (type.FullName == delegateFullName) {
                    return true;
                }
                if (type is GenericInstanceType git) {
                    return git.GenericArguments.Any(a => TypeContainsDelegate(a, delegateFullName));
                }
                if (type is ArrayType array) {
                    return TypeContainsDelegate(array.ElementType, delegateFullName);
                }
                if (type is ByReferenceType byRef) {
                    return TypeContainsDelegate(byRef.ElementType, delegateFullName);
                }
                if (type is TypeSpecification spec) {
                    return TypeContainsDelegate(spec.ElementType, delegateFullName);
                }
                return false;
            }
        }
    }
}
