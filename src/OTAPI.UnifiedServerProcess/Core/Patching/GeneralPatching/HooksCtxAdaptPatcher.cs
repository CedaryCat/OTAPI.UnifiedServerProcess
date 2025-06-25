using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Two scenarios exist for contextualizing functions, requiring HookEvents to implement two corresponding adaptations:
    /// <para>1. Specific static functions will be transformed into instance methods of contextualized entity classes, requiring the sender to be updated from null to the contextual instance.</para>
    /// <para>2. Most instance functions will have their TrackingParameter lists prepended with a RootContext TrackingParameter, necessitating the addition of a RootContext field in EventArgs.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public class HooksCtxAdaptPatcher(ILogger logger, MethodCallGraph callGraph) : GeneralPatcher(logger), IJumpSitesCacheFeature
    {
        public override string Name => nameof(HooksCtxAdaptPatcher);
        public override void Patch(PatcherArguments arguments) {
            var hookEventDelegate = arguments.MainModule.GetType("HookEvents.HookDelegate")
                ?? throw new Exception("HookEvents.HookDelegate is not defined.");
            var mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();

            foreach (var type in arguments.MainModule.GetAllTypes()) {
                if (!type.GetRootDeclaringType().Namespace.OrdinalStartsWith("HookEvents.")) {
                    continue;
                }
                if (type.BaseType?.Name == "MulticastDelegate") {
                    continue;
                }
                foreach (var invokeMethod in type.Methods) {
                    if (!invokeMethod.Name.OrdinalStartsWith("Invoke")) {
                        continue;
                    }
                    var methodId = invokeMethod.GetIdentifier();
                    if (!callGraph.MediatedCallGraph.TryGetValue(methodId, out var callData)) {
                        continue;
                    }
                    var containingMethod = callData.UsedByMethods.Single();
                    if (mappedMethods.originalToContextBound.TryGetValue(containingMethod.GetIdentifier(), out var convertedMethod)) {
                        containingMethod = convertedMethod;
                    }
                    ProcessMethod(arguments, hookEventDelegate, mappedMethods, containingMethod, invokeMethod);
                }
            }
        }

        public void ProcessMethod(
            PatcherArguments arguments,
            TypeDefinition hookEventDelegate,
            ContextBoundMethodMap mappedMethod,
            MethodDefinition containingMethod,
            MethodDefinition invokeMethod) {

            var module = arguments.MainModule;

            if (arguments.ContextTypes.TryGetValue(containingMethod.DeclaringType.FullName, out var singletonType)
                && singletonType.IsReusedSingleton
                && singletonType.ReusedSingletonMethods.ContainsKey(containingMethod.GetIdentifier())) {
                return;
            }

            foreach (var instruction in containingMethod.Body.Instructions.ToArray()) {
                var callInvokeMethod = instruction;

                if (!callInvokeMethod.MatchCall(out var invokeMethodRef)) {
                    continue;
                }
                if (invokeMethodRef.Name != invokeMethod.Name || invokeMethodRef.DeclaringType.FullName != invokeMethod.DeclaringType.FullName) {
                    continue;
                }
                var invokePaths = MonoModCommon.Stack.AnalyzeParametersSources(containingMethod, instruction, this.GetMethodJumpSites(containingMethod));
                if (invokePaths.Length != 1) {
                    throw new Exception("OTAPI HookEvents must have only one path in invoking.");
                }
                var invokePath = invokePaths[0];
                if (invokePath.ParametersSources[0].Instructions.Length != 1) {
                    throw new Exception("OTAPI HookEvents must use only one instruction in loading instance (ldnull or ldarg.0)");
                }
                var ldInstanceForInvoke = invokePath.ParametersSources[0].Instructions[0];

                var createDelegate = invokePath.ParametersSources[1].Instructions.Last();
                if (!createDelegate.MatchNewobj(out var createDelegateCtor) || createDelegateCtor.DeclaringType.Resolve().BaseType?.Name != "MulticastDelegate") {
                    throw new Exception("Unexpected createDelegate");
                }

                var createDelegatePaths = MonoModCommon.Stack.AnalyzeParametersSources(containingMethod, createDelegate, this.GetMethodJumpSites(containingMethod));

                if (createDelegatePaths.Length != 1) {
                    throw new Exception("OTAPI HookEvents must have only one path in invoking.");
                }
                var createDelegatePath = createDelegatePaths[0];
                if (createDelegatePath.ParametersSources[0].Instructions.Length != 1) {
                    throw new Exception("OTAPI HookEvents must use only one instruction in loading instance (ldnull or ldarg.0)");
                }

                var loadInstanceForDeleCtor = createDelegatePath.ParametersSources[0].Instructions.Single();
                var loadMethodPointer = createDelegatePath.ParametersSources[1].Instructions.Last();
                var ldftnMethod = (MethodReference)loadMethodPointer.Operand;

                if (ldftnMethod.HasThis && ldInstanceForInvoke.OpCode == OpCodes.Ldnull) {
                    throw new Exception("Unexpected ldftn with this");
                }
                if (!mappedMethod.originalToContextBound.TryGetValue(ldftnMethod.GetIdentifier(), out var convertedMethod)) {
                    return;
                }

                loadMethodPointer.Operand = convertedMethod;

                // context-bound by static-instance-conversion
                if (ldInstanceForInvoke.OpCode == OpCodes.Ldnull && arguments.OriginalToContextType.TryGetValue(ldftnMethod.DeclaringType.FullName, out var convertedType)) {

                    var eventFieldName = invokeMethod.Name["Invoke".Length..];
                    var eventField = invokeMethod.DeclaringType.GetField(eventFieldName);
                    var oldFieldType = (GenericInstanceType)eventField.FieldType;
                    var oldFieldFullName = eventField.FullName;

                    var eventTypeWithContext = new GenericInstanceType(hookEventDelegate);
                    eventTypeWithContext.GenericArguments.Add(convertedMethod.DeclaringType);
                    eventTypeWithContext.GenericArguments.Add(oldFieldType.GenericArguments[0]);

                    eventField.FieldType = eventTypeWithContext;

                    var theEvent = invokeMethod.DeclaringType.GetEvent(eventFieldName);
                    theEvent.EventType = eventTypeWithContext;

                    CastEventMethod(invokeMethod.DeclaringType.GetMethod("add_" + eventFieldName));
                    CastEventMethod(invokeMethod.DeclaringType.GetMethod("remove_" + eventFieldName));

                    void CastEventMethod(MethodDefinition method) {
                        method.Parameters[0].ParameterType = eventTypeWithContext;
                        foreach (var local in method.Body.Variables) {
                            local.VariableType = eventTypeWithContext;
                        }
                        foreach (var inst in method.Body.Instructions) {
                            if (inst.Operand is FieldReference eventFieldRef && eventFieldRef.FullName == oldFieldFullName) {
                                inst.Operand = new FieldReference(eventField.Name, eventField.FieldType, eventField.DeclaringType);
                            }
                            else if (inst.Operand is TypeReference typeRef && typeRef.FullName == oldFieldType.FullName) {
                                inst.Operand = eventTypeWithContext;
                            }
                            else if (inst.Operand is GenericInstanceMethod generic) {
                                for (int i = 0; i < generic.GenericArguments.Count; i++) {
                                    if (generic.GenericArguments[i].FullName == oldFieldType.FullName) {
                                        generic.GenericArguments[i] = eventTypeWithContext;
                                    }
                                }
                            }
                            else if (inst.Operand is GenericInstanceType genericType) {
                                for (int i = 0; i < genericType.GenericArguments.Count; i++) {
                                    if (genericType.GenericArguments[i].FullName == oldFieldType.FullName) {
                                        genericType.GenericArguments[i] = eventTypeWithContext;
                                    }
                                }
                            }
                        }
                    }

                    var newFieldFullName = eventField.FullName;

                    foreach (var inst in invokeMethod.Body.Instructions) {
                        if (inst.Operand is FieldReference eventFieldRef && eventFieldRef.FullName == oldFieldFullName) {
                            inst.Operand = new FieldReference(eventField.Name, eventField.FieldType, eventField.DeclaringType);
                        }
                        if (inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) {

                            var invokeEventMethod = (MethodReference)inst.Operand;
                            if (invokeEventMethod.Name != "Invoke") {
                                continue;
                            }

                            var eventTypeRef = new TypeReference(hookEventDelegate.Namespace, hookEventDelegate.Name, module, module);
                            var genericParamThis = new GenericParameter(eventTypeRef);
                            var genericParamArgs = new GenericParameter(eventTypeRef);
                            eventTypeRef.GenericParameters.Add(genericParamThis);
                            eventTypeRef.GenericParameters.Add(genericParamArgs);

                            var eventTypeGenInstance = new GenericInstanceType(eventTypeRef);
                            eventTypeGenInstance.GenericArguments.Add(convertedMethod.DeclaringType);
                            eventTypeGenInstance.GenericArguments.Add(oldFieldType.GenericArguments[0]);

                            var invokeEventRef = new MethodReference("Invoke", module.TypeSystem.Void, eventTypeGenInstance) {
                                HasThis = true
                            };
                            invokeEventRef.Parameters.Add(new ParameterDefinition(genericParamThis));
                            invokeEventRef.Parameters.Add(new ParameterDefinition(genericParamArgs));
                            inst.Operand = invokeEventRef;
                        }
                    }

                    // instance method cast to delegate should load 'this'
                    ldInstanceForInvoke.OpCode = loadInstanceForDeleCtor.OpCode = OpCodes.Ldarg_0;
                    ldInstanceForInvoke.Operand = loadInstanceForDeleCtor.Operand = null;
                    // modify invoke method's first TrackingParameter from object to converted type
                    invokeMethod.Parameters[0].ParameterType = invokeMethodRef.Parameters[0].ParameterType = convertedType.ContextTypeDef;
                }
                // context-bound by add root context
                else {
                    var delegateDef = createDelegateCtor.DeclaringType.Resolve();
                    var invokeDef = delegateDef.GetMethod("Invoke");
                    var beginInvokeDef = delegateDef.GetMethod("BeginInvoke");

                    invokeDef.Parameters.Insert(0, new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                    beginInvokeDef.Parameters.Insert(0, new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));

                    PatchingCommon.InsertParamAndRemapIndices(invokeMethod.Body, 2, new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));

                    // ReferencedParameters:
                    // sender, originalMethodDelegate, rootContext, otherArgs...
                    // so we need to insert root context at index 2
                    invokeMethodRef.Parameters.Insert(2, new ParameterDefinition(arguments.RootContextDef));
                    Instruction insertRootContextBeforeTarget;
                    if (invokePath.ParametersSources.Length == 2) {
                        insertRootContextBeforeTarget = callInvokeMethod;
                    }
                    else {
                        insertRootContextBeforeTarget = invokePath.ParametersSources[2].Instructions.First();
                    }
                    var containingMethodIL = containingMethod.Body.GetILProcessor();
                    if (containingMethod.IsStatic) {
                        containingMethodIL.InsertBeforeSeamlessly(ref insertRootContextBeforeTarget, Instruction.Create(OpCodes.Ldarg_0));
                    }
                    else {
                        containingMethodIL.InsertBeforeSeamlessly(ref insertRootContextBeforeTarget, Instruction.Create(OpCodes.Ldarg_1));
                    }

                    var createEventArgs = invokeMethod.Body.Instructions[0];
                    if (!createEventArgs.MatchNewobj(out var createEventArgsCtor)) {
                        throw new Exception("Unexpected createEventArgs");
                    }
                    var eventArgsTypeDef = createEventArgsCtor.DeclaringType.Resolve();
                    var rootContextField = new FieldDefinition(Constants.RootContextFieldName, FieldAttributes.Public, arguments.RootContextDef);
                    eventArgsTypeDef.Fields.Add(rootContextField);

                    if (invokeMethod.Body.Variables.Count != 1 || invokeMethod.Body.Variables[0].VariableType.FullName != eventArgsTypeDef.FullName) {
                        throw new Exception("Unexpected invokeMethod");
                    }

                    var invokeMethodIL = invokeMethod.Body.GetILProcessor();
                    var insertInitRootContextBeforeTarget = createEventArgs.Next;
                    invokeMethodIL.InsertBeforeSeamlessly(ref insertInitRootContextBeforeTarget, [
                        Instruction.Create(OpCodes.Dup),
                        Instruction.Create(OpCodes.Ldarg_2),
                        Instruction.Create(OpCodes.Stfld, rootContextField)
                    ]);
                }
            }
        }
    }
}
