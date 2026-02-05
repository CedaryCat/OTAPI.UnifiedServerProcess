using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.FieldFilterPatching
{
    public class DelegateWithCtxParamProcessor(TypeDefinition rootContextDef) : IFieldFilterArgProcessor, IJumpSitesCacheFeature
    {
        public string Name => nameof(DelegateWithCtxParamProcessor);

        GenericInstanceType AdjustDelegateRef(GenericInstanceType type) {
            if (type.GenericArguments.First().FullName != rootContextDef.FullName) {
                type.GenericArguments.Insert(0, rootContextDef);
            }

            if (type.ElementType.GenericParameters.Count != type.GenericArguments.Count) {
                type.ElementType.GenericParameters.Insert(0, new GenericParameter(type.ElementType));
                type.ElementType.Name = type.Name.Split('`')[0] + "`" + type.ElementType.GenericParameters.Count;
            }

            return type;
        }
        static GenericInstanceType CloneInstance(GenericInstanceType type) {
            var duplicateElement = new TypeReference(type.ElementType.Namespace, type.ElementType.Name, type.ElementType.Module, type.ElementType.Scope);
            foreach (var _ in type.ElementType.GenericParameters) {
                duplicateElement.GenericParameters.Add(new GenericParameter(duplicateElement));
            }
            var duplicateInstance = new GenericInstanceType(duplicateElement);
            foreach (var ga in type.GenericArguments) {
                duplicateInstance.GenericArguments.Add(ga);
            }
            return duplicateInstance;
        }

        public void Apply(LoggedComponent logger, ref FilterArgumentSource source) {

            var hookTypeDef = source.MainModule.GetType("Terraria.DataStructures.PlacementHook");
            var hookField = hookTypeDef.GetField("hook");

            hookField.FieldType = AdjustDelegateRef(CloneInstance((GenericInstanceType)hookField.FieldType));

            foreach (var type in source.MainModule.GetAllTypes()) {
                foreach (var method in type.Methods) { 
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (var inst in method.Body.Instructions) {
                        if (inst.Operand is FieldReference { DeclaringType.FullName: "Terraria.DataStructures.PlacementHook", Name: "hook" } fieldRef) {

                            fieldRef.FieldType = AdjustDelegateRef(CloneInstance((GenericInstanceType)fieldRef.FieldType));

                            if (inst.OpCode.Code is Code.Ldfld) {
                                foreach (var consumer in MonoModCommon.Stack.TraceStackValueConsumers(method, inst)) {
                                    if (consumer.OpCode.Code is Code.Call or Code.Callvirt) {
                                        if (consumer.Operand is not MethodReference methodRef) {
                                            throw new NotSupportedException("Not supported");
                                        }

                                        if (methodRef.DeclaringType.FullName is "System.Delegate" or "System.MulticastDelegate") {
                                            continue;
                                        }

                                        var declaringType = CloneInstance((GenericInstanceType)methodRef.DeclaringType);

                                        foreach (var param in methodRef.Parameters) {
                                            if (param.ParameterType is not GenericParameter gp) {
                                                continue;
                                            }
                                            if (gp.Owner is not TypeReference tr || tr.FullName != declaringType.ElementType.FullName) {
                                                continue;
                                            }
                                            param.ParameterType = declaringType.ElementType.GenericParameters[gp.Position];
                                        }
                                        methodRef.DeclaringType = AdjustDelegateRef(declaringType);
                                    }
                                    else if (consumer.OpCode.FlowControl is not FlowControl.Cond_Branch) {
                                        throw new NotSupportedException("Not supported");
                                    }
                                }
                            }
                            else if (inst.OpCode.Code is Code.Stfld) {
                                if (method is not { DeclaringType.FullName: "Terraria.DataStructures.PlacementHook", Name: ".ctor" }) {
                                    throw new NotSupportedException("Not supported");
                                }
                                method.Parameters[0].ParameterType = AdjustDelegateRef(CloneInstance((GenericInstanceType)method.Parameters[0].ParameterType));
                            }
                            else {
                                throw new NotSupportedException("Not supported");
                            }
                        }

                        if (inst is { OpCode.Code: Code.Call or Code.Newobj, Operand: MethodReference { Name: ".ctor", DeclaringType.FullName: "Terraria.DataStructures.PlacementHook" } hookCtor }) {

                            hookCtor.Parameters[0].ParameterType = AdjustDelegateRef(CloneInstance((GenericInstanceType)hookCtor.Parameters[0].ParameterType));

                            var path = MonoModCommon.Stack.AnalyzeParametersSources(method, inst, this.GetMethodJumpSites(method)).Single();
                            var funcParam = path.ParametersSources[0];

                            if (funcParam.Instructions[^1] is { OpCode.Code: Code.Ldnull }) {
                                continue;
                            }

                            if (funcParam.Instructions[^1] is { OpCode.Code: Code.Newobj, Operand: MethodReference delegateCtor }) {
                                delegateCtor.DeclaringType = AdjustDelegateRef(CloneInstance((GenericInstanceType)delegateCtor.DeclaringType));
                            }
                        }
                    }
                }
            }
        }
    }
}
