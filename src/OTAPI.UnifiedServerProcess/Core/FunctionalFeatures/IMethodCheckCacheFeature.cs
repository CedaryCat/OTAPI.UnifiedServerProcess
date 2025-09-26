using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Patching;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IMethodCheckCacheFeature
    {
        MethodCallGraph MethodCallGraph { get; }
    }
    public static class MethodCheckCacheFeatureExtension
    {
        public static bool AddPredefineMethodUsedContext<TFeature>(
            this TFeature _,
            MethodDefinition method) {
            return PredefineMethodUsedContext.Add(method.GetIdentifier());
        }
        public static bool AddPredefineMethodUsedContext<TFeature>(
            this TFeature _,
            string methodId) {
            return PredefineMethodUsedContext.Add(methodId);
        }
        static readonly Dictionary<string, bool> overwriteContextBoundCheck = [];
        public static void ForceOverrideContextBoundCheck<TFeature>(
            this TFeature _,
            string methodId, bool isContextBound) {
            overwriteContextBoundCheck[methodId] = isContextBound;
        }
        public static bool ForceOverrideContextBoundCheck<TFeature>(
            this TFeature _,
            MethodDefinition method, bool isContextBound) {
            return overwriteContextBoundCheck[method.GetIdentifier()] = isContextBound;
        }
        private static bool ParamCheck(MethodReferenceData referenceData, MethodDefinition callee, out bool shouldAddToCheckList) {
            shouldAddToCheckList = true;
            if (!callee.HasBody) {
                return false;
            }
            if (referenceData.implicitCallMode is ImplicitCallMode.Inheritance && referenceData.DirectlyCalledMethod.Module.Name != callee.Module.Name) {
                shouldAddToCheckList = false;
                return false;
            }
            if (callee.Parameters.Count != 0 && callee.Parameters[0].ParameterType.FullName == Constants.RootContextFullName) {
                return true;
            }
            if (callee.DeclaringType.Fields.Any(f => f.FieldType.FullName == Constants.RootContextFullName)) {
                // It is implicit reference to context and no external effect to interface constraint, so we should not add to check list
                if (referenceData.DirectlyCalledMethod != callee && referenceData.implicitCallMode is not ImplicitCallMode.None) {
                    shouldAddToCheckList = false;
                    return false;
                }
                return true;
            }
            return false;
        }
        public static bool CheckUsedContextBoundField<TFeature>(
            this TFeature point,
            IDictionary<string, FieldDefinition> instanceConvdFieldOrigMap,
            MethodDefinition checkMethod,
            bool useCache = true)
            where TFeature : IMethodCheckCacheFeature {

            if (!checkMethod.HasBody) {
                return false;
            }

            var methodId = checkMethod.GetIdentifier();

            if (overwriteContextBoundCheck.TryGetValue(methodId, out bool isContextBound)) {
                return isContextBound;
            }

            if (useCache && checkUsedContextBountFieldCache.TryGetValue(methodId, out bool value) && value) {
                return value;
            }

            var callGraph = point.MethodCallGraph;
            var inheritanceGraph = callGraph.MethodInheritanceGraph;

            HashSet<MethodDefinition> visited = [];
            Stack<MethodDefinition> worklist = new([checkMethod]);

            while (worklist.Count > 0) {
                var currentCheck = worklist.Pop();
                if (visited.Contains(currentCheck)) {
                    continue;
                }
                visited.Add(currentCheck);
                if (!currentCheck.HasBody) {
                    continue;
                }
                foreach (var inst in currentCheck.Body.Instructions) {
                    if (inst.Operand is FieldReference field) {
                        if (field.FieldType.FullName == Constants.RootContextFullName) {
                            return CacheReturn(true, useCache, methodId);
                        }
                        if (instanceConvdFieldOrigMap.ContainsKey(field.GetIdentifier())) {
                            return CacheReturn(true, useCache, methodId);
                        }
                    }
                    if (inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) {
                        var methodRef = (MethodReference)inst.Operand;

                        if (methodRef.Name == nameof(Action.Invoke) || methodRef.Name == nameof(Action.BeginInvoke)) {
                            if (PatchingCommon.IsDelegateInjectedCtxParam(methodRef.DeclaringType)) {
                                return CacheReturn(true, useCache, methodId);
                            }
                        }

                        string? autoDeleFieldName = null;
                        if (methodRef.Name.OrdinalStartsWith("add_")) {
                            autoDeleFieldName = methodRef.Name[4..];
                        }
                        else if (methodRef.Name.OrdinalStartsWith("remove_")) {
                            autoDeleFieldName = methodRef.Name[7..];
                        }
                        if (autoDeleFieldName is null) {
                            continue;
                        }

                        var declaringType = methodRef.DeclaringType.TryResolve();
                        if (declaringType is null) {
                            continue;
                        }
                        var autoDeleField = declaringType.Fields.FirstOrDefault(f => f.Name == autoDeleFieldName);
                        if (autoDeleField is null) {
                            continue;
                        }
                        if (instanceConvdFieldOrigMap.ContainsKey(autoDeleField.GetIdentifier())) {
                            return CacheReturn(true, useCache, methodId);
                        }
                    }
                    if (inst.OpCode == OpCodes.Ldftn || inst.OpCode == OpCodes.Ldvirtftn) {
                        if (inst.Next is { OpCode.Code: Code.Newobj, Operand: MethodReference deleCtor }) {
                            if (PatchingCommon.IsDelegateInjectedCtxParam(deleCtor.DeclaringType)) {
                                continue;
                            }
                        }

                        var methodRef = (MethodReference)inst.Operand;
                        if (methodRef.DeclaringType.Name == "<>c") {
                            var mDef = methodRef.Resolve();
                            if (mDef is not null) {
                                worklist.Push(mDef);
                            }
                            else {
                                var md = new MetadataResolver(checkMethod.Module.AssemblyResolver).Resolve(methodRef);
                            }
                        }
                        else if (inheritanceGraph.CheckedMethodImplementationChains.TryGetValue(methodRef.GetIdentifier(), out var implMethods)) {
                            foreach (var implMethod in implMethods) {
                                if (implMethod.Parameters.Count != 0 && implMethod.Parameters[0].ParameterType.FullName == Constants.RootContextFullName) {
                                    return CacheReturn(true, useCache, methodId);
                                }
                                worklist.Push(implMethod);
                            }
                        }
                    }
                }

                if (currentCheck.Body.Instructions.Count == 1
                    && (currentCheck.Body.Instructions[0].OpCode == OpCodes.Ret || currentCheck.Body.Instructions[0].OpCode == OpCodes.Throw)) {
                    continue;
                }
                if (currentCheck.Body.Instructions.Count == 2 &&
                    currentCheck.Body.Instructions[0].OpCode == OpCodes.Ldnull &&
                    currentCheck.Body.Instructions[1].OpCode == OpCodes.Ret) {
                    continue;
                }

                var currentId = currentCheck.GetIdentifier();

                if (callGraph.MediatedCallGraph.TryGetValue(currentId, out var calldata)) {
                    foreach (var useds in calldata.UsedMethods) {
                        if (useds.implicitCallMode is ImplicitCallMode.Delegate) {
                            continue;
                        }
                        foreach (var callee in useds.ImplementedMethods()) {

                            if (overwriteContextBoundCheck.TryGetValue(callee.GetIdentifier(), out bool isCalleeContextBound)) {
                                if (isCalleeContextBound) {
                                    return CacheReturn(true, useCache, methodId);
                                }
                            }
                            else {
                                if (PredefineMethodUsedContext.Contains(callee.GetIdentifier())) {
                                    return CacheReturn(true, useCache, methodId);
                                }
                                if (ParamCheck(useds, callee, out var shouldAddToCheckList)) {
                                    return CacheReturn(true, useCache, methodId);
                                }
                                if (shouldAddToCheckList) {
                                    worklist.Push(callee);
                                }
                                if (callee.Name == ".ctor" && callee.DeclaringType.Name.OrdinalStartsWith('<')) {
                                    foreach (var autoGenerate in callee.DeclaringType.Methods) {
                                        if (autoGenerate.Name == ".ctor") {
                                            continue;
                                        }
                                        if (ParamCheck(useds, autoGenerate, out shouldAddToCheckList)) {
                                            return CacheReturn(true, useCache, methodId);
                                        }
                                        if (shouldAddToCheckList) {
                                            worklist.Push(autoGenerate);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return CacheReturn(false, useCache, methodId);
        }

        private static bool CacheReturn(bool result, bool doCache, string methodId) {
            if (doCache) {
                return checkUsedContextBountFieldCache[methodId] = result;
            }
            return result;
        }
        static readonly Dictionary<string, bool> checkUsedContextBountFieldCache = [];
        static readonly HashSet<string> PredefineMethodUsedContext = [];
    }
}
