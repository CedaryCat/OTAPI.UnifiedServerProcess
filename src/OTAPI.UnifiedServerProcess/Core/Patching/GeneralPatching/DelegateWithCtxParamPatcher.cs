using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.IO;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    public class DelegateWithCtxParamPatcher(ILogger logger) : GeneralPatcher(logger)
    {
        public override string Name => nameof(DelegateWithCtxParamPatcher);

        public override void Patch(PatcherArguments arguments) {
            foreach (var type in arguments.MainModule.GetAllTypes()) {
                foreach (var method in type.Methods.ToArray()) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (var inst in method.Body.Instructions) {
                        if (inst.OpCode.Code is not Code.Ldftn and not Code.Ldvirtftn) {
                            continue;
                        }
                        var targetRef = (MethodReference)inst.Operand;
                        //if (!targetRef.Name.OrdinalStartsWith("<")) {
                        //    continue;
                        //}
                        var consumers = MonoModCommon.Stack.TraceStackValueConsumers(method, inst);
                        if (consumers.Length != 1 || consumers[0].OpCode.Code is not Code.Newobj) {
                            continue;
                        }
                        var ctorRef = (MethodReference)consumers[0].Operand;
                        if (!ctorRef.DeclaringType.IsDelegate()) {
                            continue;
                        }
                        if (!PatchingCommon.IsDelegateInjectedCtxParam(ctorRef.DeclaringType)) {
                            continue;
                        }
                        var targetDef = targetRef.TryResolve();
                        if (targetDef is not null && 
                            !arguments.OriginalToContextType.ContainsKey(targetRef.DeclaringType.FullName) && 
                            !arguments.RootContextFieldToAdaptExternalInterface.ContainsKey(targetRef.DeclaringType.FullName)) {
                            throw new NotImplementedException();
                        }

                        if (targetRef.Parameters.FirstOrDefault()?.ParameterType.FullName == arguments.RootContextDef.FullName) {
                            continue;
                        }

                        var originalId = targetRef.GetIdentifier(withTypeName: false);

                        targetRef.Parameters.Insert(0, new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                        targetDef = targetRef.TryResolve();
                        if (targetDef is not null) { 
                            continue; // already created
                        }

                        // Create a method with a static root context parameter to adapt to the signature,
                        // but the implementation is merely to forward to the instance method of the corresponding context.
                        if (!targetRef.HasThis && arguments.OriginalToContextType.TryGetValue(targetRef.DeclaringType.FullName, out var contextTypeData)) {
                            var transfieredMethod = contextTypeData.ContextTypeDef.Methods.Single(m => m.GetIdentifier(withTypeName: false) == originalId);

                            var att = transfieredMethod.Attributes;
                            att |= MethodAttributes.Static;
                            var methodWithRootParam = new MethodDefinition(transfieredMethod.Name, att, transfieredMethod.ReturnType);

                            methodWithRootParam.Parameters.Add(new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                            foreach (var p in transfieredMethod.Parameters) {
                                methodWithRootParam.Parameters.Add(p.Clone());
                            }

                            var body = methodWithRootParam.Body = new MethodBody(methodWithRootParam);
                            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                            foreach (var fieldAccess in contextTypeData.nestedChain) {
                                body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, fieldAccess));
                            }
                            foreach (var p in methodWithRootParam.Parameters.Skip(1)) {
                                body.Instructions.Add(MonoModCommon.IL.BuildParameterLoad(methodWithRootParam, body, p));
                            }

                            body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, transfieredMethod));
                            body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                            contextTypeData.originalType.Methods.Add(methodWithRootParam);
                            continue;
                        }

                        // Create a overloaded method. The "root" parameter is not used and is only for compatibility with the signature.
                        if (arguments.RootContextFieldToAdaptExternalInterface.TryGetValue(targetRef.DeclaringType.FullName, out var rootField)) {
                            var typeDef = targetRef.DeclaringType.Resolve();
                            var originalMethod = typeDef.Methods.Single(m => m.GetIdentifier(withTypeName: false) == originalId);

                            var att = originalMethod.Attributes;
                            att &= ~MethodAttributes.Static;
                            var methodWithRootParam = new MethodDefinition(originalMethod.Name, att, originalMethod.ReturnType) {
                                DeclaringType = typeDef
                            };

                            methodWithRootParam.Parameters.Add(new ParameterDefinition(Constants.RootContextParamName, ParameterAttributes.None, arguments.RootContextDef));
                            foreach (var p in originalMethod.Parameters) {
                                methodWithRootParam.Parameters.Add(p.Clone());
                            }

                            var body = methodWithRootParam.Body = new MethodBody(methodWithRootParam);

                            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                            foreach (var p in methodWithRootParam.Parameters.Skip(1)) {
                                body.Instructions.Add(MonoModCommon.IL.BuildParameterLoad(methodWithRootParam, body, p));
                            }

                            body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, originalMethod));
                            body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                            typeDef.Methods.Add(methodWithRootParam);
                            continue;
                        }

                        throw new NotImplementedException();
                    }
                }
            }
        }
    }
}
