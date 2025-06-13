using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// After contextualization transformation of static types, the original static types are retained (as they may still contain non-convertible static members) but require cross-type access permission adjustments, particularly for accessing internal backing fields of events.
    /// <para>For non-event members, access modifiers can be directly adjusted.</para>
    /// <para>For event backing field access, we implement controlled exposure methods to either modify/return internal delegate objects or provide reference pointers through safe memory access patterns.</para>
    /// </summary>
    /// <param name="logger"></param>
    public class AdjustAccessModifiersPatcher(ILogger logger) : GeneralPatcher(logger)
    {
        public override string Name => nameof(AdjustAccessModifiersPatcher);

        public override void Patch(PatcherArguments arguments) {
            foreach (var type in arguments.MainModule.GetAllTypes()) {
                foreach (var caller in type.Methods.ToArray()) {
                    if (!caller.HasBody) {
                        continue;
                    }
                    foreach (var inst in caller.Body.Instructions) {
                        switch (inst.OpCode.Code) {
                            case Code.Call:
                            case Code.Callvirt:
                            case Code.Newobj: {
                                    var calleeRef = (MethodReference)inst.Operand;
                                    var calleeDef = calleeRef.TryResolve();
                                    if (calleeDef is null) {
                                        continue;
                                    }
                                    if (!CheckCanAccess(caller, calleeDef)) {
                                        calleeDef.IsPublic = true;
                                    }
                                }
                                break;
                            case Code.Stsfld:
                            case Code.Stfld: {
                                    var fieldRef = (FieldReference)inst.Operand;
                                    var fieldDef = fieldRef.TryResolve();
                                    if (fieldDef is null) {
                                        continue;
                                    }
                                    if (!CheckCanAccess(caller, fieldDef)) {
                                        if (fieldDef.IsPrivate
                                            && fieldDef.CustomAttributes.Any(a => a.Constructor.DeclaringType.Name == "CompilerGeneratedAttribute")
                                            && fieldDef.FieldType.TryResolve()?.BaseType?.Name == "MulticastDelegate") {

                                            var getInnerFieldMethodName = "SetEventImpl_" + fieldDef.Name;

                                            var methodDef = fieldDef.DeclaringType.Methods.FirstOrDefault(m => m.Name == getInnerFieldMethodName);
                                            if (methodDef is null) {
                                                methodDef = new MethodDefinition(getInnerFieldMethodName, MethodAttributes.Public, arguments.MainModule.TypeSystem.Void);
                                                methodDef.Parameters.Add(new ParameterDefinition(fieldDef.FieldType));
                                                var body = methodDef.Body = new MethodBody(methodDef);
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // this
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1)); // value
                                                body.Instructions.Add(Instruction.Create(OpCodes.Stfld, fieldDef));
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                                                fieldDef.DeclaringType.Methods.Add(methodDef);
                                            }
                                            inst.OpCode = OpCodes.Call;
                                            inst.Operand = methodDef;
                                        }
                                        else {
                                            fieldDef.IsPublic = true;
                                        }
                                    }
                                }
                                break;
                            case Code.Ldsfld:
                            case Code.Ldfld: {
                                    var fieldRef = (FieldReference)inst.Operand;
                                    var fieldDef = fieldRef.TryResolve();
                                    if (fieldDef is null) {
                                        continue;
                                    }
                                    if (!CheckCanAccess(caller, fieldDef)) {
                                        if (fieldDef.IsPrivate
                                            && fieldDef.CustomAttributes.Any(a => a.Constructor.DeclaringType.Name == "CompilerGeneratedAttribute")
                                            && fieldDef.FieldType.TryResolve()?.BaseType?.Name == "MulticastDelegate") {

                                            var getInnerFieldMethodName = "GetEventImpl_" + fieldDef.Name;

                                            var methodDef = fieldDef.DeclaringType.Methods.FirstOrDefault(m => m.Name == getInnerFieldMethodName);
                                            if (methodDef is null) {
                                                methodDef = new MethodDefinition(getInnerFieldMethodName, MethodAttributes.Public, fieldDef.FieldType);
                                                var body = methodDef.Body = new MethodBody(methodDef);
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // this

                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, fieldDef));
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                                                fieldDef.DeclaringType.Methods.Add(methodDef);
                                            }
                                            inst.OpCode = OpCodes.Call;
                                            inst.Operand = methodDef;
                                        }
                                        else {
                                            fieldDef.IsPublic = true;
                                        }
                                    }
                                }
                                break;
                            case Code.Ldsflda:
                            case Code.Ldflda: {
                                    var fieldRef = (FieldReference)inst.Operand;
                                    var fieldDef = fieldRef.TryResolve();
                                    if (fieldDef is null) {
                                        continue;
                                    }
                                    if (!CheckCanAccess(caller, fieldDef)) {
                                        if (fieldDef.IsPrivate
                                            && fieldDef.CustomAttributes.Any(a => a.Constructor.DeclaringType.Name == "CompilerGeneratedAttribute")
                                            && fieldDef.FieldType.TryResolve()?.BaseType?.Name == "MulticastDelegate") {

                                            var getInnerFieldMethodName = "GetEventImplAddress_" + fieldDef.Name;

                                            var methodDef = fieldDef.DeclaringType.Methods.FirstOrDefault(m => m.Name == getInnerFieldMethodName);
                                            if (methodDef is null) {
                                                methodDef = new MethodDefinition(getInnerFieldMethodName, MethodAttributes.Public, new ByReferenceType(fieldDef.FieldType));
                                                var body = methodDef.Body = new MethodBody(methodDef);
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // this

                                                body.Instructions.Add(Instruction.Create(OpCodes.Ldflda, fieldDef));
                                                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                                                fieldDef.DeclaringType.Methods.Add(methodDef);
                                            }
                                            inst.OpCode = OpCodes.Call;
                                            inst.Operand = methodDef;
                                        }
                                        else {
                                            fieldDef.IsPublic = true;
                                        }
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }


        public static bool CheckCanAccess(MethodDefinition caller, MethodDefinition calleeDef) {
            if (caller is null || calleeDef is null)
                return false;

            var callerType = caller.DeclaringType;
            var calleeTypeDef = calleeDef.DeclaringType;

            if (calleeTypeDef is null)
                return false;

            if (!IsTypeVisibleTo(callerType, calleeTypeDef)) {
                return false;
            }

            if (calleeDef.IsPublic) {
                return true;
            }
            if (calleeDef.IsFamily) {
                return IsSubclassOf(callerType, calleeTypeDef);
            }

            bool sameAssembly = callerType.Module.Assembly.Name.FullName == calleeTypeDef.Module.Assembly.Name.FullName;
            if (calleeDef.IsFamilyAndAssembly) {
                return sameAssembly && IsSubclassOf(callerType, calleeTypeDef);
            }
            if (calleeDef.IsFamilyOrAssembly) {
                return sameAssembly || IsSubclassOf(callerType, calleeTypeDef);
            }
            if (calleeDef.IsAssembly) {
                return sameAssembly;
            }
            if (calleeDef.IsPrivate) {
                return callerType.FullName == calleeTypeDef.FullName;
            }
            return false;
        }
        public static bool CheckCanAccess(MethodDefinition caller, FieldDefinition fieldDef) {
            if (caller is null || fieldDef is null)
                return false;

            var callerType = caller.DeclaringType;
            var calleeTypeDef = fieldDef.DeclaringType;

            if (calleeTypeDef is null)
                return false;

            if (!IsTypeVisibleTo(callerType, calleeTypeDef)) {
                return false;
            }

            if (fieldDef.IsPublic) {
                return true;
            }
            if (fieldDef.IsFamily) {
                return IsSubclassOf(callerType, calleeTypeDef);
            }

            bool sameAssembly = callerType.Module.Assembly.Name.FullName == calleeTypeDef.Module.Assembly.Name.FullName;
            if (fieldDef.IsFamilyAndAssembly) {
                return sameAssembly && IsSubclassOf(callerType, calleeTypeDef);
            }
            if (fieldDef.IsFamilyOrAssembly) {
                return sameAssembly || IsSubclassOf(callerType, calleeTypeDef);
            }
            if (fieldDef.IsAssembly) {
                return sameAssembly;
            }
            if (fieldDef.IsPrivate) {
                return callerType.FullName == calleeTypeDef.FullName;
            }
            return false;
        }

        private static bool IsTypeVisibleTo(TypeDefinition callerType, TypeDefinition calleeType) {
            // Public types are always visible
            if (calleeType.IsPublic)
                return true;

            if (callerType.FullName == calleeType.FullName) {
                return true;
            }

            // Check if the types are in the same assembly
            bool sameAssembly = callerType.Module.Assembly.Name.FullName == calleeType.Module.Assembly.Name.FullName;

            // Handle the visibility of nested types
            if (calleeType.IsNested) {
                TypeDefinition current = calleeType;
                while (current.IsNested) {
                    TypeDefinition declaringType = current.DeclaringType.Resolve();

                    if (declaringType is null)
                        return false;

                    if (!IsNestedTypeVisibleTo(declaringType, current, callerType, sameAssembly))
                        return false;

                    current = declaringType;
                }
                // The outermost non-nested type must be in the same assembly and not public
                return sameAssembly && !current.IsPublic;
            }
            // Non-nested types: Same assembly and not public (i.e., internal)
            return sameAssembly;
        }

        private static bool IsNestedTypeVisibleTo(TypeDefinition declaringType, TypeDefinition nestedType, TypeDefinition callerType, bool sameAssembly) {
            if (nestedType.IsNestedPublic)
                return true;

            if (nestedType.IsNestedAssembly && sameAssembly)
                return true;

            if (nestedType.IsNestedFamily)
                return IsSubclassOf(callerType, declaringType);

            if (nestedType.IsNestedFamilyOrAssembly)
                return sameAssembly || IsSubclassOf(callerType, declaringType);

            if (nestedType.IsNestedPrivate)
                return AreTypesNestedWithin(callerType, declaringType);

            return false;
        }

        private static bool IsSubclassOf(TypeDefinition type, TypeDefinition baseType) {
            TypeDefinition current = type;
            while (current != null) {
                if (current.BaseType is null)
                    return false;

                TypeDefinition currentBase = current.BaseType.Resolve();
                if (currentBase is null)
                    return false;

                if (currentBase.FullName == baseType.FullName)
                    return true;

                current = currentBase;
            }
            return false;
        }

        private static bool AreTypesNestedWithin(TypeDefinition callerType, TypeDefinition targetType) {
            TypeDefinition? current = callerType;
            while (current != null) {
                if (current.FullName == targetType.FullName)
                    return true;

                current = current.DeclaringType?.TryResolve();
            }
            return false;
        }
    }
}
