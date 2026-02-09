using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    /// <summary>
    /// If the implementation of a function that implements a certain interface uses context-related content,
    /// <para>we need to evaluate whether to modify the interface definition to add a RootContext Parameter to achieve context attachment</para>
    /// <para>or to introduce a RootContext field within the instance to achieve context attachment</para>
    /// <para>based on: </para>
    /// <para>1. Whether the interface is defined in an external assembly;</para>
    /// <para>2. Ensuring the consistency of all implementations of the interface in the tail module.</para>
    /// </summary>
    /// <param name="methodCallGraph"></param>
    public class ExternalInterfaceProcessor(MethodCallGraph methodCallGraph) : IGeneralArgProcessor, IMethodCheckCacheFeature
    {
        readonly MethodInheritanceGraph inheritanceGraph = methodCallGraph.MethodInheritanceGraph;
        public MethodCallGraph MethodCallGraph => methodCallGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            Dictionary<string, MethodDefinition> fixedInterfaceMethods = [];
            Dictionary<string, TypeDefinition> implicitContexts = [];
            Dictionary<string, TypeDefinition> implicitContextsIncremental = [];
            ModuleDefinition modules = source.MainModule;
            var contextTypes = source.OriginalToContextType.Values.ToDictionary(t => t.ContextTypeDef.FullName, t => t.ContextTypeDef).ToImmutableDictionary();

            foreach (TypeDefinition? type in modules.GetAllTypes()) {
                if (type.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                if (source.OriginalToContextType.TryGetValue(type.FullName, out ContextTypeData? contextType) && contextType.IsReusedSingleton) {
                    continue;
                }
                if (contextTypes.ContainsKey(type.FullName)) {
                    continue;
                }
                if (AnyExternalInterfaceUsedContext(source, type, fixedInterfaceMethods)) {
                    implicitContextsIncremental.Add(type.FullName, type);
                    AddRootContextField(source, type);
                }
            }
            DiffusionFixInterface(source, fixedInterfaceMethods, implicitContexts, implicitContextsIncremental);
            ContextualStaticField(source, implicitContexts, modules, contextTypes);
        }

        private void ContextualStaticField(PatcherArgumentSource source, Dictionary<string, TypeDefinition> implicitContexts, ModuleDefinition modules, ImmutableDictionary<string, TypeDefinition> contextTypes) {
            foreach (TypeDefinition? type in modules.GetAllTypes().ToArray()) {
                if (type.Name.OrdinalStartsWith('<')) {
                    continue;
                }
                foreach (FieldDefinition? field in type.Fields) {
                    if (!field.IsStatic) {
                        continue;
                    }
                    TypeDefinition? fieldType = field.FieldType.TryResolve();
                    if (fieldType is null || !implicitContexts.ContainsKey(fieldType.FullName)) {
                        continue;
                    }
                    if (contextTypes.ContainsKey(fieldType.FullName)) {
                        continue;
                    }
                    if (source.OriginalToInstanceConvdField.ContainsKey(field.GetIdentifier())) {
                        continue;
                    }
                    if (!source.OriginalToContextType.TryGetValue(field.DeclaringType.FullName, out ContextTypeData? contextType)) {
                        contextType = new ContextTypeData(field.DeclaringType, source.RootContextDef, methodCallGraph.MediatedCallGraph, ref source.OriginalToContextType);
                    }
                    var newfield = new FieldDefinition(field.Name, field.Attributes & ~FieldAttributes.Static, field.FieldType);
                    newfield.CustomAttributes.AddRange(field.CustomAttributes.Select(c => c.Clone()));
                    contextType.ContextTypeDef.Fields.Add(newfield);
                    source.OriginalToInstanceConvdField.Add(field.GetIdentifier(), newfield);
                }
            }
        }

        private void DiffusionFixInterface(PatcherArgumentSource source, Dictionary<string, MethodDefinition> fixedInterfaceMethods, Dictionary<string, TypeDefinition> implicitContexts, Dictionary<string, TypeDefinition> implicitContextsIncremental) {
            foreach (ContextTypeData type in source.OriginalToContextType.Values) {
                implicitContextsIncremental.Add(type.ContextTypeDef.FullName, type.ContextTypeDef);
            }

            while (implicitContextsIncremental.Count > 0) {
                TypeDefinition[] types = implicitContextsIncremental.Values.ToArray();
                foreach (TypeDefinition? type in types) {
                    implicitContexts.TryAdd(type.FullName, type);
                }

                implicitContextsIncremental.Clear();

                foreach (TypeDefinition? type in types) {
                    foreach (MethodDefinition? method in type.Methods) {
                        if (method.IsStatic) {
                            continue;
                        }
                        if (!methodCallGraph.MethodInheritanceGraph.ImmediateInheritanceChains.TryGetValue(method.GetIdentifier(), out MethodDefinition[]? baseMethods)) {
                            continue;
                        }
                        List<MethodDefinition> interfaceMethods = [];
                        foreach (MethodDefinition interfaceMethod in baseMethods) {
                            if (!interfaceMethod.DeclaringType.IsInterface) {
                                continue;
                            }
                            if (fixedInterfaceMethods.ContainsKey(interfaceMethod.GetIdentifier())) {
                                continue;
                            }
                            interfaceMethods.Add(interfaceMethod);
                        }
                        if (interfaceMethods.Count == 0) {
                            continue;
                        }
                        if (!this.CheckUsedContextBoundField(source.OriginalToInstanceConvdField, method)) {
                            continue;
                        }
                        foreach (MethodDefinition interfaceMethod in interfaceMethods) {
                            fixedInterfaceMethods.TryAdd(interfaceMethod.GetIdentifier(), interfaceMethod);
                            foreach (MethodDefinition impl in methodCallGraph.MethodInheritanceGraph.RawMethodImplementationChains[interfaceMethod.GetIdentifier()]) {
                                if (impl.DeclaringType.IsInterface) {
                                    continue;
                                }
                                if (impl.DeclaringType.Name.OrdinalStartsWith('<')) {
                                    continue;
                                }
                                if (!implicitContexts.ContainsKey(impl.DeclaringType.FullName)) {
                                    implicitContextsIncremental.TryAdd(impl.DeclaringType.FullName, impl.DeclaringType);
                                }
                            }
                        }
                    }
                }

                foreach (TypeDefinition incremental in implicitContextsIncremental.Values) {
                    AddRootContextField(source, incremental);
                }
            }
        }

        private bool AnyExternalInterfaceUsedContext(PatcherArgumentSource source, TypeDefinition type, Dictionary<string, MethodDefinition> fixedInterfaceMethods) {
            foreach (MethodDefinition? methodDef in type.Methods) {
                Dictionary<string, MethodDefinition> allInheritances = [];
                if (inheritanceGraph.ImmediateInheritanceChains.TryGetValue(methodDef.GetIdentifier(), out MethodDefinition[]? baseMethods)) {
                    foreach (MethodDefinition baseMethod in baseMethods) {
                        allInheritances.TryAdd(baseMethod.GetIdentifier(), baseMethod);
                    }
                }
                if (inheritanceGraph.CheckedMethodImplementationChains.TryGetValue(methodDef.GetIdentifier(), out MethodDefinition[]? implMethods)) {
                    foreach (MethodDefinition implMethod in implMethods) {
                        allInheritances.TryAdd(implMethod.GetIdentifier(), implMethod);
                    }
                }
                if (AnyExternalInterfaceMethodUsedContext(source, [.. allInheritances.Values], fixedInterfaceMethods)) {
                    return true;
                }
            }
            return false;
        }

        public bool AnyExternalInterfaceMethodUsedContext(PatcherArgumentSource source, MethodDefinition[] checkMethods, Dictionary<string, MethodDefinition> fixedInterfaceMethods) {
            bool anyExternalInterface = false;
            foreach (MethodDefinition check in checkMethods) {
                if (check.DeclaringType.IsInterface && check.DeclaringType.Module.Name != source.MainModule.Name) {
                    fixedInterfaceMethods.TryAdd(check.GetIdentifier(), check);
                    anyExternalInterface = true;
                    break;
                }
            }
            if (anyExternalInterface) {
                foreach (MethodDefinition check in checkMethods) {
                    if (this.CheckUsedContextBoundField(source.OriginalToInstanceConvdField, check)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void AddRootContextField(PatcherArgumentSource source, TypeDefinition type) {
            TypeDefinition rootContextDef = source.RootContextDef;
            if (type.Fields.Any(f => f.FieldType.FullName == rootContextDef.FullName)) {
                return;
            }
            var rootField = new FieldDefinition(Constants.RootContextFieldName, Constants.Modifiers.ContextField, rootContextDef);
            type.Fields.Add(rootField);
            source.RootContextFieldToAdaptExternalInterface.Add(type.FullName, rootField);

            foreach (MethodDefinition? ctor in type.Methods.Where(m => m.IsConstructor && !m.IsStatic)) {
                var rootParam = new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, rootContextDef);
                PatchingCommon.InsertParamAt0AndRemapIndices(ctor.Body, PatchingCommon.InsertParamMode.Insert, rootParam);
                Instruction? insertTarget = MonoModCommon.IL.GetBaseConstructorCall(ctor.Body)?.Next;
                insertTarget ??= ctor.Body.Instructions.First();
                ILProcessor ilProcessor = ctor.Body.GetILProcessor();
                ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Ldarg_0));
                ilProcessor.InsertBefore(insertTarget, MonoModCommon.IL.BuildParameterLoad(ctor, ctor.Body, rootParam));
                ilProcessor.InsertBefore(insertTarget, Instruction.Create(OpCodes.Stfld, rootField));
            }
        }
    }
}
