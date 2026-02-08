using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using NuGet.Packaging.Signing;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using static MonoMod.InlineRT.MonoModRule;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Refactors four delegate construction patterns (direct instance/static method delegates, capture-free/capturing closures) into contextualized versions.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public partial class InvocationCtxAdaptPatcher(ILogger logger, MethodCallGraph callGraph) : GeneralPatcher(logger), IContextInjectFeature, IJumpSitesCacheFeature, IMethodCheckCacheFeature
    {
        public override string Name => nameof(InvocationCtxAdaptPatcher);

        public MethodCallGraph MethodCallGraph => callGraph;

        public override void Patch(PatcherArguments arguments) {
            var module = arguments.MainModule;
            var mappedMethod = arguments.LoadVariable<ContextBoundMethodMap>();

            ClosureDataCache cachedClosureObjs = [];

            var methods = module.GetAllTypes()
                .SelectMany(t => t.Methods.Select(m => (t, m)))
                .Where(x => x.m.HasBody)
                .ToArray();

            for (int progress = 0; progress < methods.Length; progress++) {

                var (type, method) = methods[progress];

                if (!method.HasBody) {
                    continue;
                }
                var methodId = method.GetIdentifier();

                if (type.Methods.Any(m => m.Name == "mfwh_" + method.Name)) {
                    continue;
                }

                // Only context bound methods could process,
                if (!mappedMethod.contextBoundMethods.ContainsKey(methodId)
                    && !arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(type.FullName)) {
                    continue;
                }
                Progress(progress, methods.Length, $"Processing: {method.GetDebugName()}");
                ProcessMethod(arguments, mappedMethod, cachedClosureObjs, method);
            }
        }
        public class ClosureDataCache : IEnumerable<ClosureData>
        {
            int _count;
            Dictionary<string, Dictionary<MethodDefinition, ClosureData>> _data = [];
            Dictionary<string, Dictionary<MethodDefinition, int>> _cacheId = [];
            public void Add(string key, MethodDefinition md, ClosureData data) {
                if (_data.TryGetValue(key, out var sameKeys)) {
                    if (sameKeys.Count is 0) {
                        _count += 1;
                        sameKeys.Add(md, data);
                    }
                    if (sameKeys.TryGetValue(md, out var existing) && existing != data) {
                        throw new Exception();
                    }
                    return;
                }
                _count += 1;
                _data.Add(key, new() { { md, data } });
            }
            public bool TryGet(string key, [NotNullWhen(true)] out ClosureData? data) {
                if (_data.TryGetValue(key, out var sameKey)) {
                    data = sameKey.Values.Single();
                    return true;
                }
                data = null;
                return false;
            }
            public bool TryGet(string perferKey, MethodDefinition userMethod, [NotNullWhen(true)] out ClosureData? data) {
                data = null;
                return _data.TryGetValue(perferKey, out var sameKey) && sameKey.TryGetValue(userMethod, out data);
            }
            public bool ContainsKey(string key) {
                return _data.ContainsKey(key);
            }
            public string GetNameFromPreferKey(string perferKey, MethodDefinition userMethod) {
                if (!_cacheId.TryGetValue(perferKey, out var idS)) {
                    _cacheId.Add(perferKey, idS = []);
                }
                int id;
                if (!idS.TryAdd(userMethod, id = idS.Count)) {
                    id = idS[userMethod];
                }
                if (id is 0) {
                    return perferKey.Split('/').Last();
                }
                return perferKey.Split('/').Last() + id;
            }

            public IEnumerator<ClosureData> GetEnumerator() => _data.Values.SelectMany(x => x.Values).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _data.Values.SelectMany(x => x.Values).GetEnumerator();

            public int Count => _count;
        }

        public void ProcessMethod(PatcherArguments arguments, ContextBoundMethodMap mappedMethods, ClosureDataCache cachedClosureObjs, MethodDefinition method) {
            Dictionary<Instruction, int> instructionIndexes = [];
            Instruction[] copiedInstructions = [.. method.Body.Instructions];
            for (var i = 0; i < method.Body.Instructions.Count; i++) {
                instructionIndexes[method.Body.Instructions[i]] = i;
            }
            bool anyModified = false;
            var jumpSites = this.GetMethodJumpSites(method);

            int index = 0;
            while (index < copiedInstructions.Length) {
                var processingInst = copiedInstructions[index];
                Instruction? nextInst = null;

                nextInst ??= RefactorNoCaptureAnonymousMethodDelegateCache(arguments, mappedMethods, cachedClosureObjs, jumpSites, method, processingInst);
                nextInst ??= RefactorStaticMethodCastDelegate(arguments, mappedMethods, cachedClosureObjs, jumpSites, method, processingInst);
                nextInst ??= RefactorInstanceMethodCastDelegate(arguments, mappedMethods, cachedClosureObjs, jumpSites, method, processingInst);
                nextInst ??= TryAddContextToExistingClosureMethod(arguments, mappedMethods, cachedClosureObjs, jumpSites, method, processingInst);

                if (nextInst is not null) {
                    anyModified = true;
                    index = instructionIndexes[nextInst];
                    continue;
                }
                index++;
            }

            if (anyModified) {
                this.AdjustConstructorLoadRoot(arguments.RootContextDef, method, arguments.ContextTypes.ContainsKey(method.DeclaringType.FullName));
            }
        }
        Instruction? TryAddContextToExistingClosureMethod(
            PatcherArguments arguments,
            ContextBoundMethodMap mappedMethods,
            ClosureDataCache cachedClosureObjs,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            MethodDefinition userMethod,
            Instruction instruction) {

            var createDelegate = instruction;
            if (createDelegate is null || createDelegate.OpCode != OpCodes.Newobj) {
                return null;
            }
            var delegateCtor = (MethodReference)createDelegate.Operand;
            var delegateCtorDef = delegateCtor.TryResolve();
            if (delegateCtorDef is null) {
                return null;
            }
            if (delegateCtorDef.Name != ".ctor" || delegateCtorDef.DeclaringType.BaseType?.Name != "MulticastDelegate") {
                return null;
            }

            var paths = MonoModCommon.Stack.AnalyzeParametersSources(userMethod, instruction, jumpSites);
            Instruction? firstInst = null;
            foreach (var inst in paths.Select(p => p.ParametersSources[0].Instructions[0])) {
                firstInst ??= inst;
                if (firstInst != inst) {
                    return null;
                }
            }

            var closureLoadPaths = MonoModCommon.Stack.AnalyzeStackTopTypeAllPaths(
                userMethod, 
                paths[0].ParametersSources[0].Instructions.Last(),
                jumpSites);
            if (closureLoadPaths.Length != 1) {
                return null;
            }
            TypeReference closureTypeOrigRef;
            if (MonoModCommon.IL.TryGetReferencedVariable(userMethod, closureLoadPaths[0].RealPushValueInstruction, out var closureVariable)) {
                closureTypeOrigRef = closureVariable.VariableType;
            }
            else if (closureLoadPaths[0].RealPushValueInstruction is Instruction { OpCode.Code: Code.Ldfld, Operand: FieldReference ldfr } ldfldInst && 
                ldfr.DeclaringType.Name.OrdinalStartsWith("<>c__DisplayClass")) {
                var stfldInst = userMethod.Body.Instructions.Single(
                    x => 
                    x is Instruction { 
                        OpCode.Code: Code.Stfld, 
                        Operand: FieldReference stfr 
                    }
                    && stfr.FullName == ldfr.FullName);
                var path = MonoModCommon.Stack.AnalyzeInstructionArgsSources(userMethod, stfldInst, jumpSites).Single();
                closureVariable = MonoModCommon.IL.GetReferencedVariable(userMethod, path.ParametersSources[1].Instructions.Last());
                closureTypeOrigRef = closureVariable.VariableType;
            }
            else if (closureLoadPaths[0].RealPushValueInstruction.OpCode == OpCodes.Newobj) {
                closureTypeOrigRef = ((MethodReference)closureLoadPaths[0].RealPushValueInstruction.Operand).DeclaringType;
            }
            else if (closureLoadPaths[0].RealPushValueInstruction.OpCode == OpCodes.Ldarg_0) {
                closureTypeOrigRef = userMethod.DeclaringType;
            }
            else {
                return null;
            }
            // We should not resolve the closure type reference here
            // because sometime the user method have been moved to another type,
            // so the original closure definition may been removed and create a new definition in the new type

            if (!closureTypeOrigRef.Name.OrdinalStartsWith("<>c__DisplayClass")) {
                return null;
            }

            var closureMethodLdftn = paths[0].ParametersSources[1].Instructions.Single();
            var closureMethodRef = (MethodReference)closureMethodLdftn.Operand;

            if (PatchingCommon.IsDelegateInjectedCtxParam(delegateCtor.DeclaringType)) {
                return null;
            }

            var containingType = userMethod.DeclaringType;

            var perferKey = closureTypeOrigRef.FullName;


            while (containingType.Name.OrdinalStartsWith("<>")) {
                containingType = containingType.DeclaringType;
            }
            var declaringChanged = closureTypeOrigRef.DeclaringType.FullName != containingType.FullName;

            // If the closure type should be moved to another type, we need to use the new full name
            if (declaringChanged) {
                perferKey = containingType.FullName + "/" + closureTypeOrigRef.Name;
            }

            // Is processed closure, skip
            if (cachedClosureObjs.TryGet(perferKey, userMethod, out var closureObjData) &&
                closureObjData.ProcessedMethods.ContainsKey(closureMethodRef.GetIdentifier())) {
                return null;
            }

            var closureMethodDef = closureMethodRef.TryResolve();
            if (closureMethodDef is null) {
                return null;
            }

            var next = createDelegate.Next;

            if (closureVariable is null) {
                closureVariable = new VariableDefinition(closureTypeOrigRef);
                userMethod.Body.Variables.Add(closureVariable);
                var ilProcessor = userMethod.Body.GetILProcessor();
                ilProcessor.InsertAfter(closureLoadPaths[0].RealPushValueInstruction, [
                    MonoModCommon.IL.BuildVariableStore(userMethod, userMethod.Body, closureVariable),
                    MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureVariable),
                ]);
            }

            bool anyModified = false;

            //// prepare context parameters for the method, and if it hasn't been used (anyModified is false), we will remove in the end
            //PatchingCommon.BuildInstanceLoadInstrs(arguments, userMethod.Body, null, out addedparam);

            ParameterDefinition captureParam;

            if (arguments.ContextTypes.ContainsKey(containingType.FullName)
                || arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(containingType.FullName)
                || userMethod.DeclaringType.Name.OrdinalStartsWith("<>c__DisplayClass")) {
                captureParam = userMethod.Body.ThisParameter;
            }
            else if (userMethod.Parameters.Count > 0 && userMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName) {
                captureParam = userMethod.Parameters[0];
            }
            else {
                throw new Exception();
            }

            if (closureObjData is null) {
                var closureType = closureTypeOrigRef.Resolve() ?? throw new Exception();

                if (declaringChanged) {
                    anyModified = true;

                    var typeMap = new Dictionary<TypeDefinition, TypeDefinition>() {
                        { closureType.DeclaringType, containingType }
                    };
                    var option = new MonoModCommon.Structure.MapOption(typeMap);

                    var oldClosureType = closureType;
                    closureType = MonoModCommon.Structure.MemberClonedType(closureType, closureType.Name, typeMap);

                    static IEnumerable<(TypeDefinition otype, TypeDefinition ntype)> GetTypeReplacePairs(TypeDefinition oldTypeDef, TypeDefinition newTypeDef) {
                        yield return (oldTypeDef, newTypeDef);
                        foreach (var newNestedType in newTypeDef.NestedTypes) {
                            var oldNestedType = oldTypeDef.NestedTypes.Single(ont => ont.Name == newNestedType.Name);
                            foreach (var pair in GetTypeReplacePairs(oldNestedType, newNestedType)) {
                                yield return pair;
                            }
                        }
                    }
                    foreach (var (otype, ntype) in GetTypeReplacePairs(oldClosureType, closureType)) {
                        foreach (var method in ntype.Methods) {
                            if (method.IsConstructor) {
                                continue;
                            }
                            var omethod = otype.Methods.Single(m => m.GetIdentifier(withDeclaring: false) == method.GetIdentifier(withDeclaring: false));
                            mappedMethods.contextBoundMethods.Add(method.GetIdentifier(), method);
                            mappedMethods.originalToContextBound.Add(omethod.GetIdentifier(), method);
                        }
                    }

                    foreach (var oldMethod in oldClosureType.Methods) {
                        var newMethod = closureType.Methods.Single(m => m.Name == oldMethod.Name);
                        ProcessMethod(arguments, mappedMethods, cachedClosureObjs, newMethod);
                    }

                    closureVariable.VariableType = MonoModCommon.Structure.DeepMapTypeReference(closureVariable.VariableType, option);

                    // No overload in closure type, so we can only check the name and avoid managing the change of parameters in copy
                    var newClosureMethodDef = closureType.Methods.Single(m => m.Name == closureMethodDef.Name);
                    newClosureMethodDef.Body = MonoModCommon.Structure.DeepMapMethodBody(closureMethodDef, newClosureMethodDef, option) ?? throw new Exception();
                    closureMethodDef = newClosureMethodDef;

                    var oldCreateClosure = userMethod.Body.Instructions.Single(
                        i =>
                        i.OpCode == OpCodes.Newobj
                        && ((MethodReference)i.Operand).DeclaringType.TryResolve()?.FullName == oldClosureType.FullName);

                    ((MethodReference)oldCreateClosure.Operand).DeclaringType = closureVariable.VariableType;

                    option = new(typeReplace: new() { { oldClosureType, closureType } });

                    foreach (var inst in userMethod.Body.Instructions) {
                        if (inst.Operand is TypeReference typeRef) {
                            inst.Operand = MonoModCommon.Structure.DeepMapTypeReference(typeRef, option);
                        }
                        else if (inst.Operand is MethodReference methodRef) {
                            inst.Operand = MonoModCommon.Structure.DeepMapMethodReference(methodRef, option);
                        }
                        else if (inst.Operand is FieldReference fieldRef) {
                            var fieldDef = fieldRef.Resolve();
                            if (fieldDef is null) {
                                continue;
                            }
                            var declaringType = fieldRef.DeclaringType;
                            if (option.TypeReplaceMap.TryGetValue(declaringType.Resolve(), out var mappedDeclaringType)
                                && mappedDeclaringType.Fields.Any(f => f.Name == fieldDef.Name && f.IsStatic == fieldDef.IsStatic)) {
                                declaringType = MonoModCommon.Structure.DeepMapTypeReference(fieldRef.DeclaringType, option);
                            }
                            inst.Operand = new FieldReference(fieldRef.Name,
                                MonoModCommon.Structure.DeepMapTypeReference(fieldRef.FieldType, option),
                                declaringType);
                        }
                    }

                    var oldClosureDeclaringType = oldClosureType.DeclaringType;
                    oldClosureType.DeclaringType.NestedTypes.Remove(oldClosureType);
                    // keep the original declaring type, because it might be used later
                    oldClosureType.DeclaringType = oldClosureDeclaringType;
                }
                closureObjData = ClosureData.CreateClosureDataFromExisting(closureType, closureVariable, [
                    new(userMethod, captureParam)
                ]);
            }

            foreach (var inst in closureMethodDef.Body.Instructions.ToArray()) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                        HandleLoadStaticField(inst, closureMethodDef, false, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Ldsflda:
                        HandleLoadStaticField(inst, closureMethodDef, true, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(inst, closureMethodDef, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(inst, closureMethodDef, arguments, mappedMethods, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                }
            }
            // this.AdjustConstructorLoadRoot(arguments.RootContextDef, closureMethodDef);
            if (anyModified || this.CheckUsedContextBoundField(arguments.InstanceConvdFieldOrgiMap, closureMethodDef)) {
                anyModified = true;
            }
            if (anyModified) {
                cachedClosureObjs.Add(closureObjData.ClosureType.FullName, userMethod, closureObjData);
                closureObjData.ApplyMethod(containingType, userMethod, closureMethodDef, jumpSites);
                if (!declaringChanged) {
                    ProcessMethod(arguments, mappedMethods, cachedClosureObjs, closureMethodDef);
                }
                return next;
            }
            return null;
        }
        static Instruction? RefactorStaticMethodCastDelegate(
            PatcherArguments arguments,
            ContextBoundMethodMap mappedMethods,
            ClosureDataCache cachedClosureObjs,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            MethodDefinition userMethod,
            Instruction instruction) {

            var ldnull = instruction;
            if (ldnull is null || ldnull.OpCode != OpCodes.Ldnull) {
                return null;
            }
            var ldftn = ldnull.Next;
            if (ldftn is null || ldftn.OpCode != OpCodes.Ldftn) {
                return null;
            }
            var invocationRef = (MethodReference)ldftn.Operand;
            var createDelegate = ldftn.Next;
            if (createDelegate is null || createDelegate.OpCode != OpCodes.Newobj) {
                return null;
            }
            var delegateCtor = (MethodReference)createDelegate.Operand;
            var delegateCtorDef = delegateCtor.TryResolve();
            if (delegateCtorDef is null) {
                return null;
            }
            if (delegateCtorDef.Name != ".ctor" || delegateCtorDef.DeclaringType.BaseType?.Name != "MulticastDelegate") {
                return null;
            }

            if (PatchingCommon.IsDelegateInjectedCtxParam(delegateCtor.DeclaringType)) {
                return null;
            }

            if (!mappedMethods.originalToContextBound.TryGetValue(invocationRef.GetIdentifier(), out var contextBoundInvocation)) {
                if (!mappedMethods.contextBoundMethods.TryGetValue(invocationRef.GetIdentifier(), out contextBoundInvocation)) {
                    return null;
                }
            }

            bool invocationContextBoundImplicit = false;
            var invocationDeclaringType = contextBoundInvocation.DeclaringType;
            if (arguments.ContextTypes.TryGetValue(invocationDeclaringType.FullName, out var invocationContextTypeData)) {
                invocationContextBoundImplicit = true;
            }
            else if (arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(invocationDeclaringType.FullName)) {
                invocationContextBoundImplicit = true;
            }

            FieldReference[]? rootFieldChain = null;
            var declaringType = userMethod.DeclaringType.Resolve();
            if (arguments.ContextTypes.TryGetValue(userMethod.DeclaringType.FullName, out var userMethodContextTypeData)) {
                declaringType = userMethodContextTypeData.ContextTypeDef;
                rootFieldChain = [userMethodContextTypeData.rootContextField];
            }
            else if (arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(userMethod.DeclaringType.FullName)) {
                rootFieldChain = [userMethod.DeclaringType.Fields.Single(f => f.FieldType.FullName == arguments.RootContextDef.FullName)];
            }
            else if (cachedClosureObjs.ContainsKey(userMethod.DeclaringType.FullName)) {
                List<FieldReference> chain = [];
                var root = userMethod.DeclaringType.Fields.FirstOrDefault(f => f.FieldType.FullName == arguments.RootContextDef.FullName);
                TypeDefinition checkParent = userMethod.DeclaringType;
                while (root is null) {
                    var parentField = checkParent.Fields.Single(f => f.Name == "<>4__this");
                    chain.Add(parentField);
                    checkParent = parentField.FieldType.Resolve();
                    root = checkParent.Fields.FirstOrDefault(f => f.FieldType.FullName == arguments.RootContextDef.FullName);
                }
                chain.Add(root);
                rootFieldChain = [.. chain];
            }

            // prepare context parameters for the method
            // PatchingCommon.BuildInstanceLoadInstrs(arguments, userMethod.Body, null, out addedparam);

            // we add leading zero to the method index to avoid conflict, because the method index may be changed after some patch
            var methodIndex = $"0{userMethod.DeclaringType.Methods.IndexOf(userMethod)}";
            // We set index to a large value 256 to avoid conflict with any existing closure
            var closureTypeName = "<>c__DisplayClass" + methodIndex + "_256";
            var key = $"{declaringType.FullName}/{closureTypeName}";
            var userMethodId = userMethod.GetIdentifier();

            if (!cachedClosureObjs.TryGet(key, userMethod, out var closureObjData)) {
                closureTypeName = cachedClosureObjs.GetNameFromPreferKey(key, userMethod);
                key = $"{declaringType.FullName}/{closureTypeName}";

                if (rootFieldChain is not null) {
                    cachedClosureObjs.Add(
                        key, userMethod,
                        closureObjData = ClosureData.CreateClosureByCaptureThis(arguments, declaringType, userMethod, closureTypeName));
                }
                else if (userMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName) {
                    cachedClosureObjs.Add(
                        key, userMethod,
                        closureObjData = ClosureData.CreateClosureByCaptureParam(arguments, declaringType, userMethod, closureTypeName, userMethod.Parameters[0]));
                }
                else {
                    throw new Exception("Unexpected: The first TracingParameter of the method is not the root context");
                }
            }

            if (invocationContextBoundImplicit && rootFieldChain is not null) {
                ldnull.OpCode = OpCodes.Ldarg_0;
                ldnull.Operand = null;
                ldftn.Operand = MonoModCommon.Structure.CreateMethodReference(invocationRef, contextBoundInvocation);

                if (invocationDeclaringType.FullName != declaringType.FullName) {
                    var ilProcessor = userMethod.Body.GetILProcessor();
                    ilProcessor.InsertAfter(ldnull, [
                        ..rootFieldChain.Select(f => Instruction.Create(OpCodes.Ldfld, f)),
                        ..invocationContextTypeData!.nestedChain.Select(f => Instruction.Create(OpCodes.Ldfld, f))
                    ]);
                }
                return createDelegate.Next;
            }

            // map the generic relationship in the method def (if exists)
            var generatedMethod = MonoModCommon.Structure.DeepMapMethodDef(contextBoundInvocation, new(), false);
            foreach (var param in generatedMethod.Parameters) {
                param.HasConstant = false;
                param.HasDefault = false;
                param.IsOptional = false;
            }
            generatedMethod.Attributes = MethodAttributes.Public | MethodAttributes.HideBySig;
            generatedMethod.HasThis = true;
            var rootParam = generatedMethod.Parameters.FirstOrDefault(p => p.ParameterType.FullName == arguments.RootContextDef.FullName);
            if (rootParam is not null) {
                generatedMethod.Parameters.Remove(rootParam);
            }

            generatedMethod.Body = new MethodBody(generatedMethod);
            generatedMethod.DeclaringType = closureObjData.ClosureType;
            generatedMethod.Name = $"<{invocationRef.Name}>b__0{closureObjData.ClosureType.Methods.Count}";
            var generatedMethodBody = generatedMethod.Body.Instructions;

            var closureField = closureObjData.Captures.First().CaptureField;
            var mapOption = MonoModCommon.Structure.MapOption.Create(providers: [(closureObjData.ClosureType.DeclaringType, closureObjData.ClosureType)]);
            var fieldType = MonoModCommon.Structure.DeepMapTypeReference(closureField.FieldType, mapOption);
            var declaringTypeRef = MonoModCommon.Structure.DeepMapTypeReference(closureObjData.Closure.VariableType, mapOption);
            var fieldRef = new FieldReference(closureField.Name, fieldType, declaringTypeRef);

            // both use root context as first Parameter
            if (rootFieldChain is null && !invocationContextBoundImplicit) {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, fieldRef));
            }
            // user method first Parameter is root context, invocation method is instance method and declared in a context type
            else if (rootFieldChain is null && invocationContextBoundImplicit) {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, fieldRef));
                foreach (var field in invocationContextTypeData!.nestedChain) {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, field));
                }
            }
            // user method is instance method and declared in a context type, invocation method first Parameter is root context
            else if (rootFieldChain is not null && !invocationContextBoundImplicit) {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, fieldRef));
                generatedMethodBody.AddRange(rootFieldChain.Select(f => Instruction.Create(OpCodes.Ldfld, f)));
            }
            // both are instance method and declared in a context type, but in different context
            else {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, fieldRef));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, userMethodContextTypeData!.rootContextField));
                foreach (var field in invocationContextTypeData!.nestedChain) {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, field));
                }
            }

            foreach (var param in generatedMethod.Parameters) {
                generatedMethodBody.Add(MonoModCommon.IL.BuildParameterLoad(generatedMethod, generatedMethod.Body, param));
            }
            invocationRef = MonoModCommon.Structure.DeepMapMethodReference(invocationRef, mapOption);
            generatedMethodBody.Add(Instruction.Create(OpCodes.Call, MonoModCommon.Structure.CreateMethodReference(invocationRef, contextBoundInvocation)));
            generatedMethodBody.Add(Instruction.Create(OpCodes.Ret));

            closureObjData.ApplyMethod(declaringType, userMethod, generatedMethod, jumpSites);

            var loadClosure = MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure);
            ldnull.OpCode = loadClosure.OpCode;
            ldnull.Operand = loadClosure.Operand;

            ldftn.Operand = MonoModCommon.Structure.CreateMethodReference(invocationRef, generatedMethod);

            return createDelegate.Next;
        }
        Instruction? RefactorInstanceMethodCastDelegate(
            PatcherArguments arguments,
            ContextBoundMethodMap mappedMethods,
            ClosureDataCache cachedClosureObjs,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            MethodDefinition userMethod,
            Instruction instruction) {
            var module = arguments.MainModule;

            var createDelegate = instruction;
            if (createDelegate is null || createDelegate.OpCode != OpCodes.Newobj) {
                return null;
            }
            var delegateCtor = (MethodReference)createDelegate.Operand;
            var delegateCtorDef = delegateCtor.TryResolve();
            if (delegateCtorDef is null) {
                return null;
            }
            if (delegateCtorDef.Name != ".ctor" || delegateCtorDef.DeclaringType.BaseType?.Name != "MulticastDelegate") {
                return null;
            }

            if (PatchingCommon.IsDelegateInjectedCtxParam(delegateCtor.DeclaringType)) {
                return null;
            }

            var paths = MonoModCommon.Stack.AnalyzeParametersSources(userMethod, instruction, this.GetMethodJumpSites(userMethod));
            var types = paths.Select(p => MonoModCommon.Stack.AnalyzeStackTopType(userMethod, p.ParametersSources[0].Instructions.Last(), this.GetMethodJumpSites(userMethod)))
                .Where(t => t is not null)
                .OfType<TypeReference>()
                .Distinct()
                .ToArray();

            // is static method cast to delegate, with null instance
            if (types.Length == 0) {
                return null;
            }
            // is closure method, skip and give control to 'TryAddContextToExistingClosureMethod'
            if (types.Any(t => t.Name.OrdinalStartsWith("<>c__DisplayClass"))) {
                return null;
            }

            // allow multiple instance paths, but only one load method pointer
            HashSet<Instruction> loadMethodPointerSet = [];

            var loadInstanceIfSinglePath = paths[0].ParametersSources[0].Instructions;

            bool instanceIsValueType = false;

            if (types.Length == 1
                && types[0].FullName == module.TypeSystem.Object.FullName
                && paths.Length == 1
                && loadInstanceIfSinglePath.Length == 3
                && loadInstanceIfSinglePath[1].OpCode == OpCodes.Ldobj
                && loadInstanceIfSinglePath[2].OpCode == OpCodes.Box) {

                types[0] = (TypeReference)loadInstanceIfSinglePath[2].Operand;
                instanceIsValueType = true;
            }

            bool instanceIsSelf =
                !instanceIsValueType
                && userMethod.HasThis
                && paths.Length == 1
                && loadInstanceIfSinglePath.Length == 1
                && loadInstanceIfSinglePath[0].OpCode == OpCodes.Ldarg_0;

            foreach (var path in paths) {
                var loadMethodPointerInsts = path.ParametersSources[1].Instructions;
                if (loadMethodPointerInsts.Length == 1 && loadMethodPointerInsts[0].OpCode == OpCodes.Ldftn) {
                    loadMethodPointerSet.Add(loadMethodPointerInsts[0]);
                }
                else if (loadMethodPointerInsts.Length == 2 && loadMethodPointerInsts[0].OpCode == OpCodes.Dup && loadMethodPointerInsts[1].OpCode == OpCodes.Ldvirtftn) {
                    loadMethodPointerSet.Add(loadMethodPointerInsts[1]);
                }
                else {
                    throw new NotSupportedException($"Special delegate cast not supported: {userMethod.Name}");
                }
            }

            var loadMethodPointerInst = loadMethodPointerSet.Single();
            bool isLdvirtftn = loadMethodPointerInst.OpCode == OpCodes.Ldvirtftn;
            var invocationRef = (MethodReference)loadMethodPointerInst.Operand;

            // check if loading base method's ptr:
            bool isLoadingBaseMethod =
                // use ldftn rather than ldvirtftn
                !isLdvirtftn
                // method to load is not in tail type
                && types[0].FullName != invocationRef.DeclaringType.FullName
                // but same overload exists in tail type
                && types[0].Resolve().Methods.Any(m => m.GetIdentifier(false, arguments.RootContextDef) == invocationRef.GetIdentifier(false, arguments.RootContextDef));

            var captureType = (isLoadingBaseMethod || types.Length == 1)
                ? types.First()
                : invocationRef.DeclaringType;

            var ilProcessor = userMethod.Body.GetILProcessor();

            // Only invocationAdaptorPatcher is allowed to modify targets of ldftn and ldvirtftn to context-bound methods
            // Besides reusing methods, but their calling are correct at the first time, so just keep their original status
            if (mappedMethods.contextBoundMethods.ContainsKey(invocationRef.GetIdentifier())) {
                return null;
            }
            if (!mappedMethods.originalToContextBound.TryGetValue(invocationRef.GetIdentifier(), out var contextBoundInvocation)) {
                return null;
            }

            if (instanceIsValueType) {
                loadInstanceIfSinglePath[2].OpCode = OpCodes.Nop;
                loadInstanceIfSinglePath[2].Operand = null;
            }

            FieldReference[]? rootFieldChain = null;
            var userMethodDeclaringType = userMethod.DeclaringType.Resolve();
            if (arguments.ContextTypes.TryGetValue(userMethod.DeclaringType.FullName, out var userMethodContextTypeData)) {
                userMethodDeclaringType = userMethodContextTypeData.ContextTypeDef;
                rootFieldChain = [userMethodContextTypeData.rootContextField];
            }
            else if (arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(userMethod.DeclaringType.FullName)) {
                rootFieldChain = [userMethod.DeclaringType.Fields.Single(f => f.FieldType.FullName == arguments.RootContextDef.FullName)];
            }
            else if (cachedClosureObjs.ContainsKey(userMethod.DeclaringType.FullName)) {
                List<FieldReference> chain = [];
                var root = userMethod.DeclaringType.Fields.FirstOrDefault(f => f.FieldType.FullName == arguments.RootContextDef.FullName);
                TypeDefinition checkParent = userMethod.DeclaringType;
                while (root is null) {
                    var parentField = checkParent.Fields.Single(f => f.Name == "<>4__this");
                    chain.Add(parentField);
                    checkParent = parentField.FieldType.Resolve();
                    root = checkParent.Fields.FirstOrDefault(f => f.FieldType.FullName == arguments.RootContextDef.FullName);
                }
                chain.Add(root);
                rootFieldChain = [.. chain];
            }

            // prepare context parameters for the method
            // PatchingCommon.BuildInstanceLoadInstrs(arguments, userMethod.Body, null, out addedparam);

            // we add leading zero to the method index to avoid conflict, because the method index may be changed after some patch
            var methodIndex = $"0{userMethod.DeclaringType.Methods.IndexOf(userMethod)}";
            // We set index to closure count to avoid conflict with any existing closure
            var closureTypeName = "<>c__DisplayClass" + methodIndex + "_0" + cachedClosureObjs.Count;

            var closureObjData = cachedClosureObjs.FirstOrDefault(v => {
                if (v.ContainingMethod?.GetIdentifier() != userMethod.GetIdentifier()) {
                    return false;
                }
                if (instanceIsSelf && rootFieldChain is not null) {
                    return v.Captures.Length == 1 && v.Captures[0].CaptureField.FieldType.FullName == captureType.FullName;
                }
                else {
                    return v.Captures.Length == 2
                    && v.Captures[0].CaptureField.FieldType.FullName == arguments.RootContextDef.FullName
                    && v.Captures[1].CaptureField.FieldType.FullName == captureType.FullName;
                }
            });

            if (paths.Length != 1 || closureObjData is null) {

                VariableDefinition? local = null;
                if (!instanceIsSelf) {

                    local = new VariableDefinition(captureType);
                    userMethod.Body.Variables.Add(local);

                    if (isLdvirtftn) {
                        var dupInstance = loadMethodPointerInst.Previous;
                        var stloc = MonoModCommon.IL.BuildVariableStore(userMethod, userMethod.Body, local);
                        dupInstance.OpCode = stloc.OpCode;
                        dupInstance.Operand = local;
                    }
                    else {
                        ilProcessor.InsertBeforeSeamlessly(ref loadMethodPointerInst, MonoModCommon.IL.BuildVariableStore(userMethod, userMethod.Body, local));
                    }
                }

                var key = $"{userMethodDeclaringType.FullName}/{closureTypeName}";
                if (rootFieldChain is not null) {
                    ClosureCaptureData[] captures = [];
                    if (local is null) {
                        captures = [new(userMethod, userMethod.Body.ThisParameter)];
                    }
                    else {
                        captures = [new(userMethod, userMethod.Body.ThisParameter), new("_" + captureType.Name.Split('`').First(), local)];
                    }
                    cachedClosureObjs.Add(
                        key, userMethod,
                        closureObjData = ClosureData.CreateClosureByCaptureVariables(
                            arguments,
                            userMethodDeclaringType,
                            userMethod,
                            closureTypeName,
                            captures));
                }
                else if (userMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName) {
                    ClosureCaptureData[] captures = [];
                    if (local is null) {
                        captures = [new(userMethod, userMethod.Parameters[0]), new(userMethod, userMethod.Body.ThisParameter)];
                    }
                    else {
                        captures = [new(userMethod, userMethod.Parameters[0]), new("_" + captureType.Name.Split('`').First(), local)];
                    }
                    cachedClosureObjs.Add(
                        key, userMethod,
                        closureObjData = ClosureData.CreateClosureByCaptureVariables(
                            arguments,
                            userMethodDeclaringType,
                            userMethod,
                            closureTypeName,
                            captures));
                }
                else {
                    throw new Exception("Unexpected: The first TracingParameter of the method is not the root context");
                }
                closureObjData.Apply(userMethodDeclaringType, userMethod, jumpSites);

                var loadClosure = MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure);
                if (!instanceIsSelf) {
                    ilProcessor.InsertBeforeSeamlessly(ref loadMethodPointerInst, loadClosure);
                }
                else {
                    var loadInstance = paths[0].ParametersSources[0].Instructions[0];
                    loadInstance.OpCode = loadClosure.OpCode;
                    loadInstance.Operand = loadClosure.Operand;
                }
            }
            else if (paths[0].ParametersSources[0].Instructions.Length != 1) {
                var thisCaptureIndex = instanceIsSelf && rootFieldChain is not null ? 0 : 1;

                var loadInstance = paths[0].ParametersSources[0].Instructions[0];

                ilProcessor.InsertBeforeSeamlessly(ref loadInstance, MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure));

                var fieldDef = closureObjData.Captures[thisCaptureIndex].CaptureField;
                var fieldRef = new FieldReference(fieldDef.Name, fieldDef.FieldType, closureObjData.Closure.VariableType);

                ilProcessor.InsertBeforeSeamlessly(ref loadMethodPointerInst, [
                    Instruction.Create(OpCodes.Stfld, fieldRef),
                    MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure)
                ]);
            }
            else {
                var loadClosure = MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure);
                var loadInstance = paths[0].ParametersSources[0].Instructions[0];
                loadInstance.OpCode = loadClosure.OpCode;
                loadInstance.Operand = loadClosure.Operand;
            }

            // map the generic relationship in the method def (if exists)
            var generatedMethod = MonoModCommon.Structure.DeepMapMethodDef(contextBoundInvocation, new(), false);
            bool addedRootParam = generatedMethod.Parameters.Count > 0 && generatedMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName;
            var typedGeneratedMethod = MonoModCommon.Structure.CreateInstantiatedMethod(invocationRef);
            for (int i = 0; i < typedGeneratedMethod.Parameters.Count; i++) {
                generatedMethod.Parameters[i + (addedRootParam ? 1 : 0)].ParameterType = typedGeneratedMethod.Parameters[i].ParameterType;
            }
            generatedMethod.ReturnType = typedGeneratedMethod.ReturnType;

            generatedMethod.Attributes = MethodAttributes.Public | MethodAttributes.HideBySig;
            generatedMethod.HasThis = true;

            var generatedMethodRootParam = generatedMethod.Parameters.FirstOrDefault(p => p.ParameterType.FullName == arguments.RootContextDef.FullName);
            if (generatedMethodRootParam is not null) {
                generatedMethod.Parameters.Remove(generatedMethodRootParam);
            }

            MethodReference generatedMethodImpl = MonoModCommon.Structure.CreateMethodReference(invocationRef, contextBoundInvocation);
            if (isLoadingBaseMethod) {
                var generatedBaseCall = MonoModCommon.Structure.DeepMapMethodDef(contextBoundInvocation, new(), false);

                generatedBaseCall.Name = "<>n__" + userMethodDeclaringType.Methods.Count;
                userMethodDeclaringType.Methods.Add(generatedBaseCall);

                generatedBaseCall.Body = new(generatedBaseCall);
                generatedBaseCall.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                foreach (var p in generatedBaseCall.Parameters) {
                    generatedBaseCall.Body.Instructions.Add(MonoModCommon.IL.BuildParameterLoad(generatedBaseCall, generatedBaseCall.Body, p));
                }
                generatedBaseCall.Body.Instructions.Add(Instruction.Create(OpCodes.Call, generatedMethodImpl));
                generatedBaseCall.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                if (!generatedBaseCall.CustomAttributes.Any(x => x.AttributeType.Name is "CompilerGeneratedAttribute")) {
                    var compilerGeneratedAttribute = new TypeReference("System.Runtime.CompilerServices", "CompilerGeneratedAttribute", module, module.TypeSystem.CoreLibrary);
                    generatedBaseCall.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", module.TypeSystem.Void, compilerGeneratedAttribute) { HasThis = true }));
                }
                if (!generatedBaseCall.CustomAttributes.Any(x => x.AttributeType.Name is "DebuggerHiddenAttribute")) {
                    var debuggerHiddenAttribute = new TypeReference("System.Diagnostics", "DebuggerHiddenAttribute", module, module.TypeSystem.CoreLibrary);
                    generatedBaseCall.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", module.TypeSystem.Void, debuggerHiddenAttribute) { HasThis = true }));
                }

                generatedMethodImpl = MonoModCommon.Structure.CreateMethodReference(generatedMethodImpl, generatedBaseCall);
            }

            generatedMethod.Body = new MethodBody(generatedMethod);
            generatedMethod.DeclaringType = closureObjData.ClosureType;
            generatedMethod.Name = $"<{invocationRef.Name}>b__0{closureObjData.ClosureType.Methods.Count}";
            var generatedMethodBody = generatedMethod.Body.Instructions;

            var contextField = closureObjData.Captures[0].CaptureField;
            var thisField = closureObjData.Captures[1].CaptureField;


            var mapOption = MonoModCommon.Structure.MapOption.Create(providers: [(closureObjData.ClosureType.DeclaringType, closureObjData.ClosureType)]);
            var declaringTypeRef = MonoModCommon.Structure.DeepMapTypeReference(closureObjData.Closure.VariableType, mapOption);

            var contextFieldTypeRef = new FieldReference(contextField.Name, contextField.FieldType, declaringTypeRef);
            var thisFieldTypeRef = MonoModCommon.Structure.DeepMapTypeReference(thisField.FieldType, mapOption);
            var thisFieldRef = new FieldReference(thisField.Name, thisFieldTypeRef, declaringTypeRef);

            // method use root context as first Parameter
            if (rootFieldChain is null) {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                if (thisFieldTypeRef.IsValueType) {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldflda, thisFieldRef));
                }
                else {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, thisFieldRef));
                }
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, contextFieldTypeRef));
            }
            // user method is instance method and declared in a context type
            else {
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                if (thisFieldTypeRef.IsValueType) {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldflda, thisFieldRef));
                }
                else {
                    generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, thisFieldRef));
                }
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldarg_0));
                generatedMethodBody.Add(Instruction.Create(OpCodes.Ldfld, contextFieldTypeRef));
                generatedMethodBody.AddRange(rootFieldChain.Select(f => Instruction.Create(OpCodes.Ldfld, f)));
            }
            foreach (var param in generatedMethod.Parameters) {
                generatedMethodBody.Add(MonoModCommon.IL.BuildParameterLoad(generatedMethod, generatedMethod.Body, param));
            }
            generatedMethodImpl = MonoModCommon.Structure.DeepMapMethodReference(generatedMethodImpl, mapOption);
            generatedMethodBody.Add(Instruction.Create(isLdvirtftn ? OpCodes.Callvirt : OpCodes.Call, generatedMethodImpl));
            generatedMethodBody.Add(Instruction.Create(OpCodes.Ret));
            closureObjData.ApplyMethod(userMethodDeclaringType, userMethod, generatedMethod, jumpSites);

            loadMethodPointerInst.Operand = MonoModCommon.Structure.CreateMethodReference(invocationRef, generatedMethod);

            return createDelegate.Next;
        }
        static bool IsNoCaptureAnonymousMethod(
            Instruction checkBegin,
            [NotNullWhen(true)] out Queue<Instruction>? origBlockInstructions,
            [NotNullWhen(true)] out Instruction? nextInstruction,
            [NotNullWhen(true)] out MethodDefinition? compilerGeneratedMethodOrig,
            [NotNullWhen(true)] out MethodReference? delegateCtor) {

            if (IsNormal(checkBegin, out origBlockInstructions, out nextInstruction, out compilerGeneratedMethodOrig, out delegateCtor)) {
                return true;
            }
            if (IsSimpfied(checkBegin, out origBlockInstructions, out nextInstruction, out compilerGeneratedMethodOrig, out delegateCtor)) {
                return true;
            }
            return false;

            static bool IsSimpfied(Instruction checkBegin,
                out Queue<Instruction> origBlockInstructions,
                [NotNullWhen(true)] out Instruction? nextInstruction,
                [NotNullWhen(true)] out MethodDefinition? compilerGeneratedMethodOrig,
                [NotNullWhen(true)] out MethodReference? delegateCtor) {

                compilerGeneratedMethodOrig = null;
                nextInstruction = null;
                delegateCtor = null;

                origBlockInstructions = new Queue<Instruction>();

                if (checkBegin.OpCode != OpCodes.Ldsfld) {
                    return false;
                }
                var cachedNoCaptureClosureField = ((FieldReference)checkBegin.Operand).TryResolve();
                if (cachedNoCaptureClosureField is null || cachedNoCaptureClosureField.DeclaringType.Name != "<>c" || !cachedNoCaptureClosureField.DeclaringType.IsNested) {
                    return false;
                }
                origBlockInstructions.Enqueue(checkBegin);

                var loadMethodPtr = checkBegin.Next;
                if (loadMethodPtr is null || loadMethodPtr.OpCode != OpCodes.Ldftn) {
                    return false;
                }
                compilerGeneratedMethodOrig = ((MethodReference)loadMethodPtr.Operand).Resolve();
                origBlockInstructions.Enqueue(loadMethodPtr);

                var createDelegate = loadMethodPtr.Next;
                if (createDelegate is null || createDelegate.OpCode != OpCodes.Newobj) {
                    return false;
                }
                delegateCtor = (MethodReference)createDelegate.Operand;
                origBlockInstructions.Enqueue(createDelegate);
                nextInstruction = createDelegate.Next;

                return true;
            }
            static bool IsNormal(Instruction checkBegin,
                out Queue<Instruction> origBlockInstructions,
                [NotNullWhen(true)] out Instruction? nextInstruction,
                [NotNullWhen(true)] out MethodDefinition? compilerGeneratedMethodOrig,
                [NotNullWhen(true)] out MethodReference? delegateCtor) {

                compilerGeneratedMethodOrig = null;
                nextInstruction = null;
                delegateCtor = null;

                origBlockInstructions = new Queue<Instruction>();

                if (checkBegin.OpCode != OpCodes.Ldsfld) {
                    return false;
                }
                var cachedDelegateField = ((FieldReference)checkBegin.Operand).TryResolve();
                if (cachedDelegateField is null || cachedDelegateField.DeclaringType.Name != "<>c" || !cachedDelegateField.DeclaringType.IsNested) {
                    return false;
                }
                origBlockInstructions.Enqueue(checkBegin);

                var dupCache = checkBegin.Next;
                if (dupCache is null || dupCache.OpCode != OpCodes.Dup) {
                    return false;
                }
                origBlockInstructions.Enqueue(dupCache);

                var brTrue = dupCache.Next;
                if (brTrue is null || brTrue.OpCode != OpCodes.Brtrue) {
                    return false;
                }
                origBlockInstructions.Enqueue(brTrue);

                var popNullCache = brTrue.Next;
                if (popNullCache is null || popNullCache.OpCode != OpCodes.Pop) {
                    return false;
                }
                origBlockInstructions.Enqueue(popNullCache);

                var loadClosureCache = popNullCache.Next;
                if (loadClosureCache is null || loadClosureCache.OpCode != OpCodes.Ldsfld) {
                    return false;
                }
                var origClosureCacheField = ((FieldReference)loadClosureCache.Operand).Resolve();
                origBlockInstructions.Enqueue(loadClosureCache);

                var loadMethodPtr = loadClosureCache.Next;
                if (loadMethodPtr is null || loadMethodPtr.OpCode != OpCodes.Ldftn) {
                    return false;
                }
                compilerGeneratedMethodOrig = ((MethodReference)loadMethodPtr.Operand).Resolve();
                origBlockInstructions.Enqueue(loadMethodPtr);

                var createDelegate = loadMethodPtr.Next;
                if (createDelegate is null || createDelegate.OpCode != OpCodes.Newobj) {
                    return false;
                }
                delegateCtor = (MethodReference)createDelegate.Operand;
                origBlockInstructions.Enqueue(createDelegate);

                var dupDelegate = createDelegate.Next;
                if (dupDelegate is null || dupDelegate.OpCode != OpCodes.Dup) {
                    return false;
                }
                origBlockInstructions.Enqueue(dupDelegate);

                var cacheDelegate = dupDelegate.Next;
                if (cacheDelegate is null || cacheDelegate.OpCode != OpCodes.Stsfld || ((FieldReference)cacheDelegate.Operand).FullName != cachedDelegateField.FullName) {
                    return false;
                }
                origBlockInstructions.Enqueue(cacheDelegate);
                nextInstruction = cacheDelegate.Next;
                return true;
            }
        }
        Instruction? RefactorNoCaptureAnonymousMethodDelegateCache(
            PatcherArguments arguments,
            ContextBoundMethodMap mappedMethods,
            ClosureDataCache cachedClosureObjs,
            Dictionary<Instruction, List<Instruction>> jumpSites,
            MethodDefinition userMethod,
            Instruction instruction) {

            bool addedparam = false;
            if (instruction.OpCode != OpCodes.Ldsfld) {
                return null;
            }

            if (!IsNoCaptureAnonymousMethod(instruction, out var origBlockInstructions, out var nextInstruction, out var compilerGeneratedMethodOrig, out var delegateCtor)) {
                return null;
            }

            if (PatchingCommon.IsDelegateInjectedCtxParam(delegateCtor.DeclaringType)) {
                return null;
            }

            bool userMethodContextBoundImplicit = false;
            var declaringType = userMethod.DeclaringType.Resolve();
            if (arguments.ContextTypes.TryGetValue(userMethod.DeclaringType.FullName, out var instanceConvdType)) {
                declaringType = instanceConvdType.ContextTypeDef;
                userMethodContextBoundImplicit = true;
            }
            else if (arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(userMethod.DeclaringType.FullName)) {
                userMethodContextBoundImplicit = true;
            }
            else if (cachedClosureObjs.ContainsKey(userMethod.DeclaringType.FullName)) {
                userMethodContextBoundImplicit = true;
            }

            // prepare context parameters for the method, and if it hasn't been used (anyModified is false), we will remove in the end
            // PatchingCommon.BuildInstanceLoadInstrs(arguments, userMethod.Body, null, out addedparam);

            // Create 'this' captured closure object from the default closure method without capturing external variables
            // from:
            //      <>c.<{MethodName}>b__{MethodIndex}_{ScopeIndex}
            // to:
            //      <>c__DisplayClass{MethodIndex}_{ScopeIndex}

            var match = RegexTool.DefaultClosureMethodNameRegex().Match(compilerGeneratedMethodOrig.Name);
            if (!match.Success) {
                throw new Exception("Unexpected: The method is not a compiler-generated method in closure");
            }

            var originalMethodName = match.Groups["MethodName"].Value;
            var methodIndex = int.Parse(match.Groups["MethodIndex"].Value);
            var scopeIndex = int.Parse(match.Groups["ScopeIndex"].Value);

            // We set index to a large value 256 to make sure it doesn't conflict with any existing closure
            var closureTypeName = "<>c__DisplayClass" + methodIndex + "_256";
            var closureKey = $"{declaringType.FullName}/{closureTypeName}";

            if (!cachedClosureObjs.TryGet(closureKey, userMethod, out var closureObjData)) {
                closureTypeName = cachedClosureObjs.GetNameFromPreferKey(closureKey, userMethod);
                closureKey = $"{declaringType.FullName}/{closureTypeName}";

                if (userMethodContextBoundImplicit) {
                    closureObjData = ClosureData.CreateClosureByCaptureThis(arguments, declaringType, userMethod, closureTypeName);
                }
                else if (userMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName) {
                    closureObjData = ClosureData.CreateClosureByCaptureParam(arguments, declaringType, userMethod, closureTypeName, userMethod.Parameters[0]);
                }
                else {
                    throw new Exception("Unexpected: The first TracingParameter of the method is not the root context");
                }
            }

            var generatedMethod = MonoModCommon.Structure.DeepMapMethodDef(
                compilerGeneratedMethodOrig,
                MonoModCommon.Structure.MapOption.Create([(compilerGeneratedMethodOrig.DeclaringType.Resolve(), closureObjData.ClosureType)]),
                true);
            foreach (var param in generatedMethod.Parameters) {
                param.HasConstant = false;
                param.HasDefault = false;
                param.IsOptional = false;
            }
            generatedMethod.Attributes = MethodAttributes.Public | MethodAttributes.HideBySig;
            generatedMethod.HasThis = true;
            generatedMethod.Name = $"<{originalMethodName}>b__{scopeIndex}";
            generatedMethod.DeclaringType = closureObjData.ClosureType;

            bool anyModified = false;
            foreach (var inst in generatedMethod.Body.Instructions.ToArray()) {
                switch (inst.OpCode.Code) {
                    case Code.Ldsfld:
                        HandleLoadStaticField(inst, generatedMethod, false, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Ldsflda:
                        HandleLoadStaticField(inst, generatedMethod, true, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Stsfld:
                        HandleStoreStaticField(inst, generatedMethod, arguments, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                    case Code.Call:
                    case Code.Callvirt:
                    case Code.Newobj:
                        HandleMethodCall(inst, generatedMethod, arguments, mappedMethods, closureObjData, cachedClosureObjs, ref anyModified);
                        break;
                }
            }

            if (anyModified || this.CheckUsedContextBoundField(arguments.InstanceConvdFieldOrgiMap, generatedMethod)) {
                cachedClosureObjs.Add(closureKey, userMethod, closureObjData);
                closureObjData.ApplyMethod(declaringType, userMethod, generatedMethod, jumpSites);

                var next = nextInstruction;

                Instruction[] replaceInstructions = [
                    MonoModCommon.IL.BuildVariableLoad(userMethod, userMethod.Body, closureObjData.Closure),
                    Instruction.Create(OpCodes.Ldftn, MonoModCommon.Structure.CreateMethodReference(compilerGeneratedMethodOrig, generatedMethod)),
                    Instruction.Create(OpCodes.Newobj, delegateCtor),
                ];

                for (int i = 0; i < replaceInstructions.Length; i++) {
                    var replaced = origBlockInstructions.Dequeue();
                    replaced.OpCode = replaceInstructions[i].OpCode;
                    replaced.Operand = replaceInstructions[i].Operand;
                }
                while (origBlockInstructions.Count > 0) {
                    var removed = origBlockInstructions.Dequeue();
                    userMethod.Body.RemoveInstructionSeamlessly(this.GetMethodJumpSites(userMethod), removed);
                }

                mappedMethods.originalToContextBound.Add(compilerGeneratedMethodOrig.GetIdentifier(), generatedMethod);
                mappedMethods.contextBoundMethods.Add(generatedMethod.GetIdentifier(), generatedMethod);
                ProcessMethod(arguments, mappedMethods, cachedClosureObjs, generatedMethod);

                return next;
            }
            // remove unused root context param
            else if (addedparam) {
                PatchingCommon.RemoveParamAt0AndRemapIndices(userMethod.Body, PatchingCommon.RemoveParamMode.Remove);
                addedparam = false;
            }
            return null;
        }

        void HandleMethodCall(Instruction methodCallInstruction, MethodDefinition caller, PatcherArguments arguments, ContextBoundMethodMap mappedMethods, ClosureData closureObjData, ClosureDataCache cachedClosureObjs, ref bool anyModified) {
            var calleeRef = (MethodReference)methodCallInstruction.Operand;

            var option = MonoModCommon.Structure.MapOption.Create(providers: [(closureObjData.ClosureType.DeclaringType, closureObjData.ClosureType)]);
            calleeRef = MonoModCommon.Structure.DeepMapMethodReference(calleeRef, option);
            if (!this.AdjustMethodReferences(arguments, mappedMethods, ref calleeRef, out var contextBound, out var vanillaCallee, out var contextProvider)) {
                return;
            }

            anyModified = true;
            // Generate context loading instructions
            var loadInstanceInsts = BuildContextLoadInstrs(arguments, closureObjData, cachedClosureObjs, contextProvider);
            this.InjectContextParameterLoads(arguments, ref methodCallInstruction, out _, caller, calleeRef, vanillaCallee, contextProvider, loadInstanceInsts);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="arguments"></param>
        /// <param name="closureData"></param>
        /// <param name="contextType">If it is null, the loading context is root context</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        static Instruction[] BuildContextLoadInstrs(PatcherArguments arguments, ClosureData closureData, ClosureDataCache cachedClosureObjs, ContextTypeData? contextType) {
            List<Instruction> result = [];

            var contextField = closureData.Captures.First().CaptureField;
            var closureContextType = contextField.FieldType;

            TypeReference declaringTypeRef = closureData.ClosureType;
            if (closureData.ClosureType.HasGenericParameters) {
                var genericInstanceTypeRef = new GenericInstanceType(closureData.ClosureType);
                foreach (var genericParam in closureData.ClosureType.GenericParameters) {
                    genericInstanceTypeRef.GenericArguments.Add(genericParam);
                }
                declaringTypeRef = genericInstanceTypeRef;
            }
            var contextFieldRef = new FieldReference(contextField.Name, contextField.FieldType, declaringTypeRef);

            if (contextType is not null && closureContextType.FullName == contextType.ContextTypeDef.FullName) {
                return [
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, contextFieldRef)
                ];
            }
            if (cachedClosureObjs.TryGet(contextField.FieldType.FullName, out var nestedClosureDatas)) {
                return [
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, contextFieldRef),
                    ..BuildContextLoadInstrs(arguments, nestedClosureDatas, cachedClosureObjs, contextType).Where(inst => inst.OpCode != OpCodes.Ldarg_0),
                ];
            }
            var rootField = closureContextType.Resolve().Fields.FirstOrDefault(f => f.FieldType.FullName == arguments.RootContextDef.FullName);
            if (rootField is not null || arguments.RootContextFieldToAdaptExternalInterface.TryGetValue(closureContextType.FullName, out rootField)) {
                result.Add(Instruction.Create(OpCodes.Ldarg_0));
                result.Add(Instruction.Create(OpCodes.Ldfld, contextFieldRef));
                result.Add(Instruction.Create(OpCodes.Ldfld, rootField));
            }
            // If context of closure is member of root context
            else if (arguments.ContextTypes.TryGetValue(closureContextType.FullName, out var callerDeclaringInstanceConvdType)) {
                result.Add(Instruction.Create(OpCodes.Ldarg_0));
                result.Add(Instruction.Create(OpCodes.Ldfld, contextFieldRef));
                result.Add(Instruction.Create(OpCodes.Ldfld, callerDeclaringInstanceConvdType.rootContextField));
            }
            // elsewise, context of closure must be the root context
            else if (closureContextType.FullName == arguments.RootContextDef.FullName) {
                result.Add(Instruction.Create(OpCodes.Ldarg_0));
                result.Add(Instruction.Create(OpCodes.Ldfld, contextFieldRef));
            }
            else {
                throw new Exception($"Unexpected closure context type {closureContextType.FullName}");
            }

            if (contextType is not null) {
                foreach (var field in contextType.nestedChain) {
                    result.Add(Instruction.Create(OpCodes.Ldfld, field));
                }
            }

            return [.. result];
        }

        void HandleStoreStaticField(Instruction instruction, MethodDefinition generatedMethod, PatcherArguments arguments, ClosureData closureObjData, ClosureDataCache cachedClosureObjs, ref bool anyModified) {
            var fieldRef = (FieldReference)instruction.Operand;
            if (!arguments.InstanceConvdFieldOrgiMap.TryGetValue(fieldRef.GetIdentifier(), out var contextBoundFieldDef)) {
                return;
            }
            anyModified = true;

            ContextTypeData? contextType = null;
            if (contextBoundFieldDef.DeclaringType.FullName != arguments.RootContextDef.FullName) {
                contextType = arguments.ContextTypes[contextBoundFieldDef.DeclaringType.FullName];
            }

            var loadInstanceInsts = BuildContextLoadInstrs(arguments, closureObjData, cachedClosureObjs, contextType);
            this.InjectContextFieldStoreInstanceLoads(arguments, ref instruction, out _, generatedMethod, contextBoundFieldDef, fieldRef, loadInstanceInsts);
        }

        void HandleLoadStaticField(Instruction instruction, MethodDefinition generatedMethod, bool isAddress, PatcherArguments arguments, ClosureData closureObjData, ClosureDataCache cachedClosureObjs, ref bool anyModified) {
            var fieldRef = (FieldReference)instruction.Operand;
            if (!arguments.InstanceConvdFieldOrgiMap.TryGetValue(fieldRef.GetIdentifier(), out var contextBoundFieldDef)) {
                return;
            }

            anyModified = true;

            ContextTypeData? contextType = null;
            if (contextBoundFieldDef.DeclaringType.FullName != arguments.RootContextDef.FullName) {
                contextType = arguments.ContextTypes[contextBoundFieldDef.DeclaringType.FullName];
            }

            var loadInstanceInsts = BuildContextLoadInstrs(arguments, closureObjData, cachedClosureObjs, contextType);
            this.InjectContextFieldLoadInstanceLoads(arguments, ref instruction, out _, isAddress, generatedMethod, contextBoundFieldDef, fieldRef, loadInstanceInsts);
        }
        static partial class RegexTool
        {
            [GeneratedRegex(@"^<(?<MethodName>[^>]+)>b__(?<MethodIndex>\d+)_(?<ScopeIndex>\d+)$", RegexOptions.Compiled)]
            public static partial Regex DefaultClosureMethodNameRegex();
        }
    }
}
