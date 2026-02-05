using Microsoft.CodeAnalysis;
using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Analysis
{

    public class MethodInheritanceGraph
    {
        public readonly Dictionary<string, MethodDefinition[]> RawMethodImplementationChains;
        public readonly Dictionary<string, MethodDefinition[]> CheckedMethodImplementationChains;
        public readonly Dictionary<string, MethodDefinition[]> ImmediateInheritanceChains;

        public MethodInheritanceGraph(params ModuleDefinition[] modules) {
            var typedMethods = new Dictionary<string, Dictionary<string, MethodDefinition>>();
            var chains = new Dictionary<string, Dictionary<string, MethodDefinition>>();
            var typeOrder = GetTypesInInheritanceOrder(modules);

            foreach (var type in typeOrder) {
                typedMethods[type.FullName] = type.Methods.ToDictionary(m => m.GetIdentifier(false), m => m);
            }
            foreach (var type in typeOrder) {
                ProcessInterfaces(type, typedMethods, chains);
            }

            var interfaceInhereritance = GenerateInheritanceChains(chains);
            foreach (var kv in interfaceInhereritance) {
                kv.Value.Remove(kv.Key);
            }

            foreach (var type in typeOrder) {
                foreach (var method in type.Methods) {
                    ProcessMethod(method, interfaceInhereritance, chains);
                }
            }

            RawMethodImplementationChains = chains.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.ToArray()
            );
            CheckedMethodImplementationChains = chains.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.Where(m => m.Body != null).ToArray()
            );

            ImmediateInheritanceChains = GenerateInheritanceChains(chains).ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.ToArray()
            );
        }

        public void RemapMethodIdentifiers(IReadOnlyDictionary<string, string> oldToNew) {
            AnalysisRemap.RemapDictionaryKeysInPlace(RawMethodImplementationChains, oldToNew, nameof(RawMethodImplementationChains));
            AnalysisRemap.RemapDictionaryKeysInPlace(CheckedMethodImplementationChains, oldToNew, nameof(CheckedMethodImplementationChains));
            AnalysisRemap.RemapDictionaryKeysInPlace(ImmediateInheritanceChains, oldToNew, nameof(ImmediateInheritanceChains));
        }

        private static Dictionary<string, Dictionary<string, MethodDefinition>> GenerateInheritanceChains(Dictionary<string, Dictionary<string, MethodDefinition>> chains) {
            Dictionary<string, Dictionary<string, MethodDefinition>> immediateInheritanceChains = [];

            foreach (var kv in chains) {
                var baseMethodId = kv.Key;
                var implementations = kv.Value;
                var baseMethod = implementations[baseMethodId];

                foreach (var implementation in implementations) {
                    if (!immediateInheritanceChains.TryGetValue(implementation.Key, out var inheritance)) {
                        immediateInheritanceChains.Add(implementation.Key, inheritance = []);
                    }
                    inheritance.TryAdd(baseMethodId, baseMethod);
                }
            }

            return immediateInheritanceChains;
        }

        private static void ProcessInterfaces(
            TypeDefinition type,
            Dictionary<string, Dictionary<string, MethodDefinition>> typedMethods,
            Dictionary<string, Dictionary<string, MethodDefinition>> chains) {
            foreach (var interfaceImpl in type.Interfaces) {
                var interfaceDef = interfaceImpl.InterfaceType.Resolve();

                foreach (var interfaceMethod in interfaceDef.Methods) {
                    var typed = MonoModCommon.Structure.CreateInstantiatedMethod(interfaceMethod, interfaceImpl.InterfaceType);
                    var currentType = type;
                    while (currentType is not null) {
                        var methods = typedMethods[currentType.FullName];

                        if (methods.TryGetValue(typed.GetIdentifier(false), out var impl)) {
                            var interfaceIdentifier = interfaceMethod.GetIdentifier();
                            if (!chains.TryGetValue(interfaceIdentifier, out var interfaceChain)) {
                                chains.Add(interfaceIdentifier, interfaceChain = new() { { interfaceIdentifier, interfaceMethod } });
                            }
                            interfaceChain.TryAdd(impl.GetIdentifier(), impl);
                        }

                        currentType = currentType.BaseType?.TryResolve();
                    }
                }
            }
        }

        private static void ProcessMethod(
            MethodDefinition method,
            Dictionary<string, Dictionary<string, MethodDefinition>> interfaceInhereritance,
            Dictionary<string, Dictionary<string, MethodDefinition>> chains) {

            var identifier = method.GetIdentifier();

            if (!chains.ContainsKey(identifier)) {
                chains.Add(identifier, new() { { identifier, method } });
            }

            foreach (var baseMethod in GetImmediateBaseMethods(method, interfaceInhereritance)) {
                var baseIdentifier = baseMethod.GetIdentifier();
                if (!chains.TryGetValue(baseIdentifier, out var baseChain)) {
                    chains.Add(baseIdentifier, baseChain = new() { { baseIdentifier, baseMethod } });
                }
                baseChain.TryAdd(identifier, method);
            }
        }
        /// <summary>
        /// Returns all methods that implemented by the given method, not including the method itself
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static MethodDefinition[] GetImmediateBaseMethods(MethodDefinition method, Dictionary<string, Dictionary<string, MethodDefinition>> interfaceInhereritance) {
            HashSet<MethodDefinition> result = [];

            foreach (var ov in method.Overrides) {
                var methodDef = ov.TryResolve();
                if (methodDef is null) {
                    continue;
                }
                result.Add(methodDef);
            }

            TypeReference currentType = method.DeclaringType;
            while (currentType != null) {
                var resolved = currentType.TryResolve();
                if (resolved is null) {
                    break;
                }
                foreach (var currentMethod in resolved.Methods) {
                    var typed = MonoModCommon.Structure.CreateInstantiatedMethod(currentMethod, currentType);

                    if (typed.GetIdentifier(false) != method.GetIdentifier(false)) {
                        continue;
                    }

                    if (interfaceInhereritance.TryGetValue(currentMethod.GetIdentifier(), out var interfaceMethods)) {
                        foreach (var interfaceMethod in interfaceMethods.Values) {
                            if (!interfaceMethod.DeclaringType.IsInterface) {
                                continue;
                            }
                            result.Add(interfaceMethod);
                        }
                    }

                    if (!currentMethod.IsVirtual) {
                        continue;
                    }
                    if (currentType != method.DeclaringType) {
                        result.Add(currentMethod);
                    }
                }
                currentType = resolved.BaseType;
            }

            return result.ToArray();
        }

        private static List<TypeDefinition> GetTypesInInheritanceOrder(params ModuleDefinition[] modules) {
            var allTypes = new HashSet<TypeDefinition>();
            var sorted = new List<TypeDefinition>();

            foreach (var module in modules) {
                foreach (var type in module.GetTypes()) {
                    VisitType(type, allTypes, sorted);
                }

            }

            return sorted;
        }

        private static void VisitType(
            TypeDefinition type,
            HashSet<TypeDefinition> visited,
            List<TypeDefinition> sorted) {
            if (type is null || visited.Contains(type)) return;

            var baseType = type.BaseType?.TryResolve();
            if (baseType != null && !visited.Contains(baseType)) {
                VisitType(baseType, visited, sorted);
            }

            if (visited.Add(type)) {
                sorted.Add(type);
            }
        }
    }
}
