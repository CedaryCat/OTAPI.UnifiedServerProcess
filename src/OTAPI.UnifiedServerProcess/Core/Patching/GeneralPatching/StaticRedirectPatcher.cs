using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching {
    /// <summary>
    /// <para>Attaches context parameters to specific static methods, redirects their accesses to static fields by binding field versions to the context,</para>
    /// <para>and incrementally processes callers to propagate context parameters through invocations.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="delegateInvocationGraph"></param>
    /// <param name="methodInheritanceGraph"></param>
    /// <param name="callGraph"></param>
    public class StaticRedirectPatcher(ILogger logger,
        DelegateInvocationGraph delegateInvocationGraph,
        MethodInheritanceGraph methodInheritanceGraph,
        MethodCallGraph callGraph) : GeneralPatcher(logger), IContextInjectFeature, IMethodBehaivorFeature, IJumpSitesCacheFeature, IMethodCheckCacheFeature {

        public override string Name => nameof(StaticRedirectPatcher);
        public DelegateInvocationGraph DelegateInvocationGraph => delegateInvocationGraph;
        public MethodInheritanceGraph MethodInheritanceGraph => methodInheritanceGraph;
        public MethodCallGraph MethodCallGraph => callGraph;
        public override void Patch(PatcherArguments arguments) {
            var module = arguments.MainModule;

            var mappedMethods = new ContextBoundMethodMap();
            arguments.StoreVariable(mappedMethods);

            var convertedMethodOrigMap = mappedMethods.originalToContextBound;
            var contextBoundMethods = mappedMethods.contextBoundMethods;

            foreach (var predefined in arguments.ContextTypes.Values.Where(t => t.IsPredefined)) {
                foreach (var kv in predefined.PredefinedMethodMap) {
                    var method = kv.Value;
                    convertedMethodOrigMap.Add(kv.Key, method);
                    contextBoundMethods.Add(method.GetIdentifier(), method);
                    this.AddPredefineMethodUsedContext(kv.Key);
                }
            }
            foreach (var context in arguments.ContextTypes.Values.Where(t => !t.IsReusedSingleton)) {
                contextBoundMethods.Add(context.constructor.GetIdentifier(), context.constructor);
            }
            foreach (var reused in arguments.ContextTypes.Values.Where(t => t.IsReusedSingleton)) {
                var originalCtorId = reused.constructor.GetIdentifier(true, arguments.RootContextDef);
                convertedMethodOrigMap.Add(originalCtorId, reused.constructor);
                contextBoundMethods.Add(reused.constructor.GetIdentifier(), reused.constructor);
            }
            foreach (var interfAdapt in arguments.RootContextFieldToAdaptExternalInterface.Values) {
                foreach (var ctor in interfAdapt.DeclaringType.Methods.Where(m => m.IsConstructor && !m.IsStatic)) {
                    var originalCtorId = ctor.GetIdentifier(true, arguments.RootContextDef);
                    convertedMethodOrigMap.Add(originalCtorId, ctor);
                    contextBoundMethods.Add(ctor.GetIdentifier(), ctor);
                }
            }

            var workQueue = module.GetAllTypes()
                // skip delegate closures and auto enumerators, they will be handled by the later patcher
                .Where(t => !t.Name.StartsWith('<'))
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody && m.Name != ".cctor")
                .ToDictionary(m => m.GetIdentifier());

            int iteration = 0;
            while (workQueue.Count > 0) {
                iteration++;
                var currentWorkBatch = workQueue.ToArray();

                for (int progress = 0; progress < currentWorkBatch.Length; progress++) {
                    var method = currentWorkBatch[progress].Value;
                    var removeKey = currentWorkBatch[progress].Key;
                    var modifiedMethod = method;

                    var methodId = method.GetIdentifier();

                    Progress(iteration, progress, currentWorkBatch.Length, method.GetDebugName());
                    ProcessMethod(
                        module,
                        arguments,
                        originalToContextMethod: convertedMethodOrigMap,
                        contextBoundMethods: contextBoundMethods,
                        modifiedMethod,
                        out var modifyMode
                    );
                    workQueue.Remove(removeKey);

                    if (modifyMode == MethodModifyMode.None) {
                        continue;
                    }

                    AddNextIterationMethod(arguments, workQueue, mappedMethods, method, modifyMode, iteration, progress, currentWorkBatch.Length);
                    var anotherAccessor = FindAnotherAccessor(method);
                    if (anotherAccessor is not null && modifyMode is not MethodModifyMode.InstanceConverted) {
                        if (anotherAccessor.Parameters.Count == 0 || anotherAccessor.Parameters[0].ParameterType.FullName != arguments.RootContextDef.FullName) {
                            var rootParam = new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef);

                            var originalId = anotherAccessor.GetIdentifier();
                            PatchingCommon.InsertParamAt0AndRemapIndices(anotherAccessor.Body, PatchingCommon.InsertParamMode.Insert, rootParam);
                            var newId = anotherAccessor.GetIdentifier();

                            convertedMethodOrigMap.Add(originalId, anotherAccessor);
                            contextBoundMethods.Add(newId, anotherAccessor);

                            AddNextIterationMethod(arguments, workQueue, mappedMethods, anotherAccessor, modifyMode, iteration, progress, currentWorkBatch.Length);
                        }
                    }
                }
            }
        }
        static MethodDefinition? FindAnotherAccessor(MethodDefinition method) {
            if (!method.IsSpecialName) {
                return null;
            }
            var name = method.Name;
            if (name.StartsWith("get_")) {
                return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == string.Concat("set_", name.AsSpan(4)));
            }
            else if (name.StartsWith("set_")) {
                return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == string.Concat("get_", name.AsSpan(4)));
            }
            var nameParts = name.Split('.');
            if (nameParts.Length > 1) {
                name = nameParts[^1];
                if (name.StartsWith("get_")) {
                    nameParts[^1] = string.Concat("set_", name.AsSpan(4));
                    var methodName = string.Join('.', nameParts);
                    return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == methodName);
                }
                else if (name.StartsWith("set_")) {
                    nameParts[^1] = string.Concat("get_", name.AsSpan(4));
                    var methodName = string.Join('.', nameParts);
                    return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == methodName);
                }
            }
            return null;
        }
        void AddNextIterationMethod(
            PatcherArguments arguments, 
            Dictionary<string, MethodDefinition> workQueue,
            ContextBoundMethodMap mappedMethods,
            MethodDefinition processedMethod, 
            MethodModifyMode modifyMode, 
            int iteration, int progress, int total) {

            var module = arguments.MainModule;
            var id = processedMethod.GetIdentifier();
            var graph = callGraph.MediatedCallGraph;
            var root = arguments.RootContextDef;
            var convertedTypes = arguments.ContextTypes;

            var vanillaMethod = PatchingCommon.GetVanillaMethodRef(root, convertedTypes, processedMethod);
            var methodOrigId = vanillaMethod.GetIdentifier();

            if (graph.TryGetValue(id, out var callers) || graph.TryGetValue(methodOrigId, out callers)) {

                foreach (var caller in callers.UsedByMethods) {

                    // skip delegate closures, they will be handled by the InvocationCtxAdaptorPatcher
                    if (caller.DeclaringType.Name.StartsWith('<')) {
                        continue;
                    }
                    // skip static ctors, they will be handled by the CctorCtxAdaptPatcher
                    if (caller.Name == ".cctor") {
                        continue;
                    }

                    var redirectedCaller = caller;
                    var callerId = caller.GetIdentifier();

                    // If the caller has a context-bound version, redirect to the context-bound method
                    if (mappedMethods.originalToContextBound.TryGetValue(callerId, out var instanceConvdMethod)) {
                        redirectedCaller = instanceConvdMethod;
                        callerId = redirectedCaller.GetIdentifier();
                    }
                    // The modified method is used by the caller, so caller should be check again
                    if (workQueue.TryAdd(callerId, redirectedCaller)) {
                        Progress(iteration, progress, total, "Add: {0}", indent: 1, caller.GetDebugName());
                    }
                }
            }

            if (modifyMode.HasFlag(MethodModifyMode.AddedParam)) {

                HashSet<MethodDefinition> lowestBaseMethods = [];

                if (methodInheritanceGraph.ImmediateInheritanceChains.TryGetValue(vanillaMethod.GetIdentifier(), out var immediateInheritanceChain)) {
                    foreach (var baseMethod in immediateInheritanceChain) {
                        if (!baseMethod.DeclaringType.IsInterface) {
                            continue;
                        }
                        if (baseMethod.Module.Name != module.Name) {
                            continue;
                        }
                        lowestBaseMethods.Add(baseMethod);
                    }
                }

                MethodDefinition? lowestBaseMethod = null;
                TypeReference? baseType = processedMethod.DeclaringType.BaseType;
                while (baseType is not null) {

                    TypeDefinition? resolvedBaseType = baseType.TryResolve();
                    if (resolvedBaseType is null || resolvedBaseType.Module.Name != module.Name) {
                        break;
                    }

                    MethodDefinition? baseMethod = null;
                    foreach (var m in resolvedBaseType.Methods) {
                        if (!m.IsVirtual) {
                            continue;
                        }
                        if (m.Name != vanillaMethod.Name) {
                            continue;
                        }
                        var typed = MonoModCommon.Structure.CreateTypedMethod(m, baseType);
                        if (typed.GetIdentifier(false) != vanillaMethod.GetIdentifier(false)) {
                            continue;
                        }
                        baseMethod = m;
                        break;
                    }
                    if (baseMethod != null && baseMethod.IsVirtual) {
                        lowestBaseMethod = baseMethod;
                    }
                    baseType = resolvedBaseType.BaseType;
                }
                if (lowestBaseMethod is not null) {
                    lowestBaseMethods.Add(lowestBaseMethod);
                }

                foreach (var baseMethod in lowestBaseMethods) {
                    if (methodInheritanceGraph.RawMethodImplementationChains.TryGetValue(baseMethod.GetIdentifier(), out var inheritedMethods)) {
                        foreach (var inheritedMethod in inheritedMethods) {
                            var inheritedMethodId = inheritedMethod.GetIdentifier();
                            if (mappedMethods.originalToContextBound.ContainsKey(inheritedMethodId) || mappedMethods.contextBoundMethods.ContainsKey(inheritedMethodId)) {
                                continue;
                            }
                            var rootParam = new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef);
                            if (inheritedMethod.HasBody) {
                                PatchingCommon.InsertParamAt0AndRemapIndices(inheritedMethod.Body, PatchingCommon.InsertParamMode.Insert, rootParam);
                            }
                            else {
                                inheritedMethod.Parameters.Insert(0, rootParam);
                                if (inheritedMethod.HasOverrides) {
                                    foreach (var overrides in inheritedMethod.Overrides) {
                                        overrides.Parameters.Insert(0, rootParam);
                                    }
                                }
                            }

                            mappedMethods.originalToContextBound.Add(inheritedMethodId, inheritedMethod);
                            mappedMethods.contextBoundMethods.Add(inheritedMethod.GetIdentifier(), inheritedMethod);
                            if (graph.TryGetValue(inheritedMethodId, out callers)) {
                                foreach (var caller in callers.UsedByMethods) {
                                    // skip delegate closures, these will be handled by the InvocationAdaptorPatcher
                                    if (caller.DeclaringType.Name.StartsWith('<')) {
                                        continue;
                                    }
                                    // skip static ctors, they will be handled by the CctorCtxAdaptPatcher
                                    if (caller.Name == ".cctor") {
                                        continue;
                                    }

                                    var redirectedCaller = caller;
                                    var callerId = caller.GetIdentifier();
                                    if (mappedMethods.originalToContextBound.TryGetValue(callerId, out var convertedCaller)) {
                                        redirectedCaller = convertedCaller;
                                        callerId = convertedCaller.GetIdentifier();
                                    }
                                    if (workQueue.TryAdd(callerId, redirectedCaller)) {
                                        Progress(iteration, progress, total, "Add: {0}", indent: 1, caller.GetDebugName());
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        enum MethodModifyMode {
            None = 0,
            /// <summary>
            /// Context-bound by static-to-instance conversion (implicit-context-bound)
            /// </summary>
            InstanceConverted = 1 << 0,
            /// <summary>
            /// Context-bound by adding a root context parameter (explicit-context-bound)
            /// </summary>
            AddedParam = 1 << 1,
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="arguments"></param>
        /// <param name="originalToContextMethod">Key: original method identifier, Value: converted method</param>
        /// <param name="contextBoundMethods">Only the context-bound methods | Key: converted method identifier, Value: converted method</param>
        /// <param name="method"></param>
        /// <param name="mode">Indicates the external effect, if it is not None, all the callers of this method will be added to the work queue</param>
        /// <exception cref="Exception"></exception>
        /// <exception cref="IndexOutOfRangeException"></exception>
        void ProcessMethod(
            ModuleDefinition module,
            PatcherArguments arguments,
            Dictionary<string, MethodDefinition> originalToContextMethod,
            Dictionary<string, MethodDefinition> contextBoundMethods,
            MethodDefinition method,
            out MethodModifyMode mode) {

            mode = MethodModifyMode.None;
            bool addedParam = false;

            if (method.DeclaringType.FullName == arguments.RootContextDef.FullName) {
                return;
            }
            if (arguments.ContextTypes.TryGetValue(method.DeclaringType.FullName, out var predefined) && predefined.IsPredefined) {
                return;
            }

            var methodId = method.GetIdentifier();

            if (arguments.OriginalToContextType.TryGetValue(method.DeclaringType.FullName, out var contextType)
                || arguments.ContextTypes.TryGetValue(method.DeclaringType.FullName, out contextType)) {

                if (originalToContextMethod.TryGetValue(method.GetIdentifier(), out var convertedMethod) && !contextType.IsReusedSingleton) {
                    throw new Exception($"The method {method.GetDebugName()} has already been bound with context to {convertedMethod.GetDebugName()}, shouldn't be add to work queue");
                }

                if (method.IsStatic) {
                    if (this.CheckUsedContextBoundField(arguments.RootContextDef, arguments.InstanceConvdFieldOrgiMap, method)) {
                        method = PatchingCommon.CreateInstanceConvdMethod(method, contextType, arguments.InstanceConvdFieldOrgiMap);
                        mode = MethodModifyMode.InstanceConverted;
                        // add context-bound method for original method key
                        originalToContextMethod.Add(methodId, method);
                        methodId = method.GetIdentifier();

                        if (method.IsConstructor) {
                            // .cctor will merge into ctor, so just add once
                            contextBoundMethods.TryAdd(methodId, method);
                        }
                        else {
                            // add context-bound method for self
                            contextBoundMethods.Add(methodId, method);
                        }
                    }
                }
                else if (contextType.IsReusedSingleton && !contextBoundMethods.ContainsKey(methodId)) {
                    if (method.IsConstructor) {
                        methodId = method.GetIdentifier(true, arguments.RootContextDef);
                    }
                    method = contextType.ReusedSingletonMethods[methodId];
                    // add context-bound method for original method key
                    originalToContextMethod.Add(methodId, method);
                    methodId = method.GetIdentifier();
                    // add context-bound method for self
                    contextBoundMethods.Add(methodId, method);
                }
                else if (this.CheckUsedContextBoundField(arguments.RootContextDef, arguments.InstanceConvdFieldOrgiMap, method)) {
                    PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, null, out addedParam);
                    if (addedParam) {
                        mode |= MethodModifyMode.AddedParam;
                    }
                }
            }
            else if (this.CheckUsedContextBoundField(arguments.RootContextDef, arguments.InstanceConvdFieldOrgiMap, method)) {
                PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, null, out addedParam);
                if (addedParam) {
                    mode |= MethodModifyMode.AddedParam;
                }
            }

            foreach (var instruction in method.Body.Instructions.ToArray()) {

                switch (instruction.OpCode.Code) {
                    case Code.Ldsfld:
                        HandleLoadStaticField(instruction, method, false, out addedParam);
                        break;
                    case Code.Ldsflda:
                        HandleLoadStaticField(instruction, method, true, out addedParam);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(instruction, method, out addedParam);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(instruction, method, out addedParam);
                        break;
                    // Prepare context for InvocationAdaptorPatcher
                    case Code.Ldftn:
                    case Code.Ldvirtftn:
                        HandleLoadMethodPtr(instruction, method, out addedParam);
                        break;
                }

                if (addedParam) {
                    mode |= MethodModifyMode.AddedParam;
                }
            }

            this.AdjustConstructorLoadRoot(arguments.RootContextDef, method, arguments.ContextTypes.ContainsKey(method.DeclaringType.FullName));

            if (mode.HasFlag(MethodModifyMode.AddedParam)) {
                contextBoundMethods.Add(method.GetIdentifier(), method);
                originalToContextMethod.Add(methodId, method);
            }

            return;

            void HandleLoadMethodPtr(Instruction instruction, MethodDefinition caller, out bool addedParam) {
                addedParam = false;
                if (contextType is not null) {
                    return;
                }
                var methodRef = (MethodReference)instruction.Operand;
                var methodDef = methodRef.TryResolve();
                if (methodDef is null) {
                    return;
                }
                if (!this.CheckUsedContextBoundField(arguments.RootContextDef, arguments.InstanceConvdFieldOrgiMap, methodDef)) {
                    return;
                }
                Info("HandleLoadMethodPtr: {0} when processing {1}", methodRef.GetDebugName(), caller.GetDebugName());
                PatchingCommon.BuildInstanceLoadInstrs(arguments, caller.Body, null, out addedParam);
            }
            void HandleLoadStaticField(Instruction instruction, MethodDefinition method, bool isAddress, out bool addedParam) {

                addedParam = false;
                var fieldRef = (FieldReference)instruction.Operand;
                var field = fieldRef.TryResolve();

                if (field is null) {
                    return;
                }

                FieldDefinition? instanceConvdField;
                // If the loading field is just an context, it must come from a singleton field redirection
                if (arguments.OriginalToContextType.TryGetValue(field.FieldType.FullName, out var instanceConvdType) && instanceConvdType.IsReusedSingleton) {
                    // If it is loading the field value but address, and current method is an instance method of the context
                    // Just use 'this'
                    if (method.DeclaringType.FullName == instanceConvdType.ContextTypeDef.FullName
                        && !method.IsStatic
                        && !isAddress) {

                        instruction.OpCode = OpCodes.Ldarg_0;
                        instruction.Operand = null;
                        return;
                    }
                    // Load the field by chain: ** root context -> field 1 (context) -> ... -> field n-1 (context) -> current field **
                    // The current field will be loaded by existing instruction: ** isAddress ? OpCodes.Ldflda : OpCodes.Ldfld **
                    // So the instructions insert before the instruction is the load of field n-1
                    else {
                        instanceConvdField = instanceConvdType.nestedChain.Last();
                        // If instance-converted types doesn't have the type of the field n-1 (pararent instance of current field), it means the field n-1 is root context
                        if (!arguments.ContextTypes.TryGetValue(instanceConvdField.DeclaringType.FullName, out instanceConvdType)) {
                            instanceConvdType = null;
                        }
                    }
                }
                // If the loading field is a member field of a context but context itself
                else if (arguments.InstanceConvdFieldOrgiMap.TryGetValue(field.FullName, out instanceConvdField)) {
                    var declaringType = instanceConvdField.DeclaringType;
                    // pararent instance of current field must be a existing context
                    instanceConvdType = arguments.ContextTypes[declaringType.FullName];
                }
                else {
                    return;
                }
                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, instanceConvdType, out addedParam);
                this.InjectContextFieldLoadInstanceLoads(arguments, ref instruction, out _, isAddress, method, instanceConvdField, fieldRef, loadInstanceInsts);
            }

            void HandleStoreStaticField(Instruction instruction, MethodDefinition method, out bool addedParam) {
                addedParam = false;
                var fieldRef = (FieldReference)instruction.Operand;
                var field = fieldRef.TryResolve();
                if (field is null) {
                    return;
                }

                FieldDefinition? instanceConvdField;
                // If the loading field is just an context, it must come from a singleton field redirection
                if (arguments.OriginalToContextType.TryGetValue(field.FieldType.FullName, out var instanceConvdType) && instanceConvdType.IsReusedSingleton) {

                    // Load the field by chain: ** root context -> field 1 (context) -> ... -> field n-1 (context) -> current field **

                    // The current field will be loaded by existing instruction: ** isAddress ? OpCodes.Ldflda : OpCodes.Ldfld **
                    // So the instructions insert before the instruction is the load of field n-1

                    instanceConvdField = instanceConvdType.nestedChain.Last();
                    // If instance-converted types doesn't have the type of the field n-1 (pararent instance of current field), it means the field n-1 is root context
                    if (!arguments.ContextTypes.TryGetValue(instanceConvdField.DeclaringType.FullName, out instanceConvdType)) {
                        instanceConvdType = null;
                    }
                }
                // If the loading field is a member field of a context but context itself
                else if (arguments.InstanceConvdFieldOrgiMap.TryGetValue(field.FullName, out instanceConvdField)) {
                    var declaringType = instanceConvdField.DeclaringType;
                    // pararent instance of current field must be a existing context
                    instanceConvdType = arguments.ContextTypes[declaringType.FullName];
                }
                else {
                    return;
                }

                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, method.Body, instanceConvdType, out addedParam);
                this.InjectContextFieldStoreInstanceLoads(arguments, ref instruction, out _, method, instanceConvdField, fieldRef, loadInstanceInsts);
            }

            void HandleMethodCall(Instruction methodCallInstruction, MethodDefinition caller, out bool addedParam) {

                addedParam = false;

                var calleeRefToAdjust = (MethodReference)methodCallInstruction.Operand;

                if (!this.AdjustMethodReferences(arguments, arguments.LoadVariable<ContextBoundMethodMap>(), ref calleeRefToAdjust, out var contextBound, out var vanillaCallee, out var contextType)) {
                    return;
                }
                var loadInstanceInsts = PatchingCommon.BuildInstanceLoadInstrs(arguments, caller.Body, contextType, out addedParam);

                this.InjectContextParameterLoads(arguments, ref methodCallInstruction, out _, caller, contextBound, calleeRefToAdjust, vanillaCallee, contextType, loadInstanceInsts);
            }
        }
    }
}
