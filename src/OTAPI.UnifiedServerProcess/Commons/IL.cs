using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Diagnostics.CodeAnalysis;

namespace OTAPI.UnifiedServerProcess.Commons
{
    public static partial class MonoModCommon
    {
        [MonoMod.MonoModIgnore]
        public static class IL
        {
            /// <summary>
            /// The name of the "this" parameter is an empty string rather than "this".
            /// </summary>
            public const string ThisParameterName = "";
            public static Instruction BuildParameterLoad(MethodDefinition method, MethodBody body, ParameterDefinition parameter) {
                if (method.HasThis && ((body is not null && body.ThisParameter == parameter) || parameter.Name == "")) {
                    return Instruction.Create(OpCodes.Ldarg_0);
                }
                else {
                    var index = method.Parameters.IndexOf(parameter) + (method.HasThis ? 1 : 0);
                    return index switch {
                        -1 => throw new ArgumentException("Parameter not found in method", nameof(parameter)),
                        0 => Instruction.Create(OpCodes.Ldarg_0),
                        1 => Instruction.Create(OpCodes.Ldarg_1),
                        2 => Instruction.Create(OpCodes.Ldarg_2),
                        3 => Instruction.Create(OpCodes.Ldarg_3),
                        _ => Instruction.Create(index < byte.MaxValue ? OpCodes.Ldarg_S : OpCodes.Ldarg, parameter)
                    };
                }
            }
            public static Instruction BuildParameterSet(MethodDefinition method, MethodBody body, ParameterDefinition parameter) {
                if (method.HasThis && ((body is not null && body.ThisParameter == parameter) || parameter.Name == "")) {
                    throw new ArgumentException("Cannot set \"this\" parameter", nameof(parameter));
                }
                else {
                    var index = method.Parameters.IndexOf(parameter) + (method.HasThis ? 1 : 0);
                    if (index < 0) {
                        throw new ArgumentException("Parameter not found in method", nameof(parameter));
                    }
                    return Instruction.Create(index < byte.MaxValue ? OpCodes.Starg_S : OpCodes.Starg, parameter);
                }
            }
            public static Instruction BuildParameterLoadAddress(MethodDefinition method, MethodBody body, ParameterDefinition parameter) {
                if (method.HasThis && ((body is not null && body.ThisParameter == parameter) || parameter.Name == "")) {
                    return Instruction.Create(OpCodes.Ldarga_S, body?.ThisParameter ?? throw new InvalidOperationException());
                }
                else {
                    var index = method.Parameters.IndexOf(parameter) + (method.HasThis ? 1 : 0);
                    if (index < 0) {
                        throw new ArgumentException("Parameter not found in method", nameof(parameter));
                    }
                    return Instruction.Create(index < byte.MaxValue ? OpCodes.Ldarga_S : OpCodes.Ldarga, parameter);
                }
            }
            public static Instruction BuildVariableLoad(MethodDefinition method, MethodBody body, VariableDefinition variable) {
                var index = method.Body.Variables.IndexOf(variable);
                if (index < 0) {
                    throw new ArgumentException("Variable not found in method", nameof(variable));
                }
                return index switch {
                    0 => Instruction.Create(OpCodes.Ldloc_0),
                    1 => Instruction.Create(OpCodes.Ldloc_1),
                    2 => Instruction.Create(OpCodes.Ldloc_2),
                    3 => Instruction.Create(OpCodes.Ldloc_3),
                    _ => Instruction.Create(index < byte.MaxValue ? OpCodes.Ldloc_S : OpCodes.Ldloc, variable)
                };
            }
            public static Instruction BuildVariableStore(MethodDefinition method, MethodBody body, VariableDefinition variable) {
                var index = method.Body.Variables.IndexOf(variable);
                if (index < 0) {
                    throw new ArgumentException("Variable not found in method", nameof(variable));
                }
                return index switch {
                    0 => Instruction.Create(OpCodes.Stloc_0),
                    1 => Instruction.Create(OpCodes.Stloc_1),
                    2 => Instruction.Create(OpCodes.Stloc_2),
                    3 => Instruction.Create(OpCodes.Stloc_3),
                    _ => Instruction.Create(index < byte.MaxValue ? OpCodes.Stloc_S : OpCodes.Stloc, variable)
                };
            }
            public static Instruction BuildVariableLoadAddress(MethodDefinition method, MethodBody body, VariableDefinition variable) {
                var index = method.Body.Variables.IndexOf(variable);
                if (index < 0) {
                    throw new ArgumentException("Variable not found in method", nameof(variable));
                }
                return Instruction.Create(index < byte.MaxValue ? OpCodes.Ldloca_S : OpCodes.Ldloca, variable);
            }
            public static ParameterDefinition GetReferencedParameter(MethodDefinition method, Instruction instruction) {
                ParameterDefinition? tmpCheck = null;
                var paramIndex = instruction.OpCode.Code switch {
                    Code.Ldarg_0 or
                    Code.Ldarg_1 or
                    Code.Ldarg_2 or
                    Code.Ldarg_3 => instruction.OpCode.Code - Code.Ldarg_0 - (method.HasThis ? 1 : 0),
                    Code.Ldarg_S or
                    Code.Ldarg or
                    Code.Ldarga_S or
                    Code.Ldarga or
                    Code.Starg_S or
                    Code.Starg => (tmpCheck = (ParameterDefinition)instruction.Operand).Index,
                    _ => throw new InvalidOperationException($"Unsupported opcode {instruction.OpCode.Code}")
                };
                if (paramIndex == -1) {
                    return method.Body?.ThisParameter ?? new ParameterDefinition("", ParameterAttributes.None, method.DeclaringType);
                }
                var param = method.Parameters[paramIndex];
                if (tmpCheck is not null && tmpCheck.Name != param.Name) {
                    throw new InvalidOperationException("Operand parameter is invalid");
                }
                return param;
            }
            public static bool TryGetReferencedParameter(MethodDefinition method, Instruction instruction, [NotNullWhen(true)] out ParameterDefinition? parameter) {
                return TryGetReferencedParameter(method, instruction, out _, out parameter);
            }
            public static bool TryGetReferencedParameter(MethodDefinition method, Instruction instruction, out int paramInnerIndex, [NotNullWhen(true)] out ParameterDefinition? parameter) {
                ParameterDefinition? tmpCheck = null;

                paramInnerIndex = instruction.OpCode.Code switch {
                    Code.Ldarg_0 => 0,
                    Code.Ldarg_1 => 1,
                    Code.Ldarg_2 => 2,
                    Code.Ldarg_3 => 3,
                    Code.Ldarg_S or
                    Code.Ldarg or
                    Code.Ldarga_S or
                    Code.Ldarga or
                    Code.Starg_S or
                    Code.Starg => (tmpCheck = (ParameterDefinition)instruction.Operand).Index + (method.HasThis ? 1 : 0),
                    _ => -1
                };

                if (paramInnerIndex == -1) {
                    parameter = null;
                    return false;
                }

                if (paramInnerIndex == 0 && method.HasThis) {
                    parameter = method.Body?.ThisParameter ?? new ParameterDefinition("", ParameterAttributes.None, method.DeclaringType);
                }
                else {
                    var paramIndex = paramInnerIndex - (method.HasThis ? 1 : 0);
                    parameter = method.Parameters[paramIndex];
                    if (tmpCheck is not null && tmpCheck.Name != parameter.Name) {
                        throw new InvalidOperationException("Operand parameter is invalid");
                    }
                }
                return true;
            }
            public static VariableDefinition GetReferencedVariable(MethodDefinition method, Instruction instruction) {
                VariableDefinition? tmpCheck = null;
                var localIndex = instruction.OpCode.Code switch {
                    Code.Ldloc_0 or Code.Stloc_0 => 0,
                    Code.Ldloc_1 or Code.Stloc_1 => 1,
                    Code.Ldloc_2 or Code.Stloc_2 => 2,
                    Code.Ldloc_3 or Code.Stloc_3 => 3,
                    Code.Ldloc_S or
                    Code.Ldloc or
                    Code.Ldloca or
                    Code.Ldloca_S or
                    Code.Stloc or
                    Code.Stloc_S or
                    Code.Stloc => (tmpCheck = (VariableDefinition)instruction.Operand).Index,
                    _ => throw new InvalidOperationException($"Unsupported opcode {instruction.OpCode.Code}")
                };

                if (tmpCheck is not null && tmpCheck != method.Body.Variables[localIndex]) {
                    throw new InvalidOperationException("Operand variable is invalid");
                }

                return method.Body.Variables[localIndex];
            }
            public static bool TryGetReferencedVariable(MethodDefinition method, Instruction instruction, [NotNullWhen(true)] out VariableDefinition? variable) {
                return TryGetReferencedVariable(method, instruction, out _, out variable);
            }
            public static bool TryGetReferencedVariable(MethodDefinition method, Instruction instruction, out int localIndex, [NotNullWhen(true)] out VariableDefinition? variable) {
                VariableDefinition? tmpCheck = null;

                localIndex = instruction.OpCode.Code switch {
                    Code.Ldloc_0 or Code.Stloc_0 => 0,
                    Code.Ldloc_1 or Code.Stloc_1 => 1,
                    Code.Ldloc_2 or Code.Stloc_2 => 2,
                    Code.Ldloc_3 or Code.Stloc_3 => 3,
                    Code.Ldloc_S or
                    Code.Ldloc or
                    Code.Ldloca or
                    Code.Ldloca_S or
                    Code.Stloc or
                    Code.Stloc_S or
                    Code.Stloc => (tmpCheck = (VariableDefinition)instruction.Operand).Index,
                    _ => -1
                };

                if (tmpCheck is not null && tmpCheck != method.Body.Variables[localIndex]) {
                    throw new ArgumentException("Operand variable is invalid", nameof(instruction));
                }

                if (localIndex == -1) {
                    variable = null;
                    return false;
                }

                variable = method.Body.Variables[localIndex];
                return true;
            }
            public static TypeReference GetMethodReturnType(MethodReference target, MethodDefinition callerContext) {
                if (target is GenericInstanceMethod genericMethod) {
                    return ResolveGenericReturnType(genericMethod, genericMethod.ReturnType);
                }
                if (target.DeclaringType is GenericInstanceType declaringGenericType) {
                    return ResolveGenericParameterInType(declaringGenericType, target.ReturnType);
                }
                return target.ReturnType;
            }
            private static TypeReference ResolveGenericReturnType(GenericInstanceMethod genericMethod, TypeReference returnType) {
                if (returnType.IsGenericParameter) {
                    var genericParam = (GenericParameter)returnType;
                    if (genericParam.Owner is MethodReference) {
                        if (genericMethod.GenericArguments.Count > genericParam.Position) {
                            return genericMethod.GenericArguments[genericParam.Position];
                        }
                    }
                    else if (genericParam.Owner is TypeReference) {
                        if (genericMethod.DeclaringType is GenericInstanceType declaringGenericType) {
                            return ResolveGenericParameterInType(declaringGenericType, returnType);
                        }
                    }
                }

                // handle nested generic type (e.g. List<T> where T is a generic type)
                if (returnType is GenericInstanceType genericReturnType) {
                    var newGenericReturnType = new GenericInstanceType(genericReturnType.ElementType);
                    foreach (var arg in genericReturnType.GenericArguments) {
                        newGenericReturnType.GenericArguments.Add(ResolveGenericReturnType(genericMethod, arg));
                    }
                    return newGenericReturnType;
                }

                return returnType;
            }
            private static TypeReference ResolveGenericParameterInType(GenericInstanceType genericType, TypeReference type) {
                if (type.IsGenericParameter) {
                    var genericParam = (GenericParameter)type;
                    if (genericParam.Owner is TypeReference && genericType.GenericArguments.Count > genericParam.Position) {
                        return genericType.GenericArguments[genericParam.Position];
                    }
                }
                return type;
            }
            public static Instruction? GetBaseConstructorCall(MethodBody ctorBody) {
                if (ctorBody.Method.Name != ".ctor") {
                    throw new ArgumentException("Method is not a constructor", nameof(ctorBody));
                }
                var ctor = ctorBody.Method;
                for (int i = 0; i < ctorBody.Instructions.Count; i++) {
                    var check = ctorBody.Instructions[i];
                    if (check is { OpCode.Code: Code.Call, Operand: MethodReference { Name: ".ctor" } checkCtor }) {
                        var checkCtorTypeDef = checkCtor.DeclaringType.Resolve();
                        if (checkCtorTypeDef.FullName == ctor.DeclaringType.BaseType.FullName || checkCtorTypeDef.FullName == ctor.DeclaringType.FullName) {
                            return check;
                        }
                    }
                }
                return null;
            }
        }
    }
}
