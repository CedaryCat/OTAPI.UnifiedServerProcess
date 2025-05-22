using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.DataModels {
    public class ClosureData {
        public TypeDefinition ClosureType;
        public MethodDefinition ClosureConstructor;
        public ClosureCaptureData[] Captures;
        public Instruction? CaptureFieldAssignment;
        public MethodDefinition? ContainingMethod;
        public VariableDefinition Closure;
        public readonly Dictionary<string, MethodDefinition> SupportMethods = [];
        public bool IsReusedClosure { get; private set; }

        private ClosureData(TypeDefinition type, MethodDefinition constructor, VariableDefinition closure, ClosureCaptureData[] captures) {
            ClosureType = type;
            ClosureConstructor = constructor;
            Closure = closure;
            Captures = captures;
        }

        [MemberNotNullWhen(true, nameof(CaptureFieldAssignment), nameof(ContainingMethod))]
        public bool Applied { get; private set; } = false;

        public void Apply(TypeDefinition declaringType, MethodDefinition containingMethod, Dictionary<Instruction, List<Instruction>> jumpSites) {
            var ilProcessor = containingMethod.Body.GetILProcessor();
            Instruction closureInitInsertBeforetarget;
            if (!IsReusedClosure) {
                declaringType.NestedTypes.Add(ClosureType);
                containingMethod.Body.Variables.Add(Closure);
                ContainingMethod = containingMethod;

                closureInitInsertBeforetarget = containingMethod.Body.Instructions.First();

                ilProcessor.InsertBefore(closureInitInsertBeforetarget, Instruction.Create(OpCodes.Newobj, new MethodReference(ClosureConstructor.Name, ClosureConstructor.ReturnType, Closure.VariableType) { HasThis = true }));
                ilProcessor.InsertBefore(closureInitInsertBeforetarget, MonoModCommon.IL.BuildVariableStore(containingMethod, containingMethod.Body, Closure));
            }
            else {
                if (declaringType.HasGenericParameters) {
                    foreach (var capture in Captures) {
                        capture.CaptureField.FieldType = MonoModCommon.Structure.DeepMapTypeReference(
                            capture.CaptureField.FieldType,
                            MonoModCommon.Structure.MapOption.Create(providers: [(declaringType, ClosureType)]));
                        ClosureType.Fields.Add(capture.CaptureField);
                    }
                }
                else {
                    ClosureType.Fields.AddRange(Captures.Select(c => c.CaptureField));
                }
                closureInitInsertBeforetarget = containingMethod.Body.Instructions
                    .Single(i => i.OpCode == OpCodes.Newobj && ((MethodReference)i.Operand).DeclaringType.TryResolve()?.FullName == ClosureType.FullName)
                    .Next
                    .Next;
            }

            foreach (var capture in Captures) {
                if (capture.CaptureVariable is ParameterDefinition param) {
                    // load closure obj
                    ilProcessor.InsertBefore(closureInitInsertBeforetarget, MonoModCommon.IL.BuildVariableLoad(containingMethod, containingMethod.Body, Closure));
                    // load capture value
                    ilProcessor.InsertBefore(closureInitInsertBeforetarget, CaptureFieldAssignment = MonoModCommon.IL.BuildParameterLoad(containingMethod, containingMethod.Body, param));
                    // store capture value in closure field
                    ilProcessor.InsertBefore(closureInitInsertBeforetarget, Instruction.Create(OpCodes.Stfld, new FieldReference(capture.CaptureField.Name, capture.CaptureField.FieldType, Closure.VariableType)));
                }
                else if (capture.CaptureVariable is VariableDefinition capturedLocal) {
                    foreach (var instruction in containingMethod.Body.Instructions.ToArray()) {
                        if (!MonoModCommon.IL.TryGetReferencedVariable(containingMethod, instruction, out var local)) {
                            continue;
                        }
                        if (local != capturedLocal) {
                            continue;
                        }
                        switch (instruction.OpCode.Code) {
                            case Code.Ldloc_0:
                            case Code.Ldloc_1:
                            case Code.Ldloc_2:
                            case Code.Ldloc_3:
                            case Code.Ldloc_S:
                            case Code.Ldloc: {
                                    var target = instruction;
                                    ilProcessor.InsertBeforeSeamlessly(ref target, MonoModCommon.IL.BuildVariableLoad(containingMethod, containingMethod.Body, Closure));
                                    target.OpCode = OpCodes.Ldfld;
                                    target.Operand = new FieldReference(capture.CaptureField.Name, capture.CaptureField.FieldType, Closure.VariableType);
                                }
                                break;
                            case Code.Ldloca_S:
                            case Code.Ldloca: {
                                    var target = instruction;
                                    ilProcessor.InsertBeforeSeamlessly(ref target, MonoModCommon.IL.BuildVariableLoad(containingMethod, containingMethod.Body, Closure));
                                    target.OpCode = OpCodes.Ldflda;
                                    target.Operand = new FieldReference(capture.CaptureField.Name, capture.CaptureField.FieldType, Closure.VariableType);
                                }
                                break;
                            case Code.Stloc_0:
                            case Code.Stloc_1:
                            case Code.Stloc_2:
                            case Code.Stloc_3:
                            case Code.Stloc_S:
                            case Code.Stloc: {
                                    HashSet<Instruction> insertBefore = [];
                                    foreach (var path in MonoModCommon.Stack.AnalyzeInstructionArgsSources(containingMethod, instruction, jumpSites)) {
                                        insertBefore.Add(path.ParametersSources[0].Instructions[0]);
                                    }
                                    foreach (var beforeTarget in insertBefore) {
                                        var target = beforeTarget;
                                        ilProcessor.InsertBeforeSeamlessly(ref target, MonoModCommon.IL.BuildVariableLoad(containingMethod, containingMethod.Body, Closure));
                                    }
                                    instruction.OpCode = OpCodes.Stfld;
                                    instruction.Operand = new FieldReference(capture.CaptureField.Name, capture.CaptureField.FieldType, Closure.VariableType);
                                }
                                break;
                        }
                    }
                }
                else {
                    throw new Exception($"Unknown capture variable type: {capture.CaptureVariable.GetType().Name}");
                }
            }

            Applied = true;
        }
        public void ApplyMethod(TypeDefinition declaringType, MethodDefinition containingMethod, MethodDefinition generatedMethod, Dictionary<Instruction, List<Instruction>> jumpSites) {
            if (!IsReusedClosure || !ClosureType.Methods.Any(m => m.GetIdentifier(false) == generatedMethod.GetIdentifier(false))) {
                ClosureType.Methods.Add(generatedMethod);
            }
            SupportMethods.Add(generatedMethod.GetIdentifier(), generatedMethod);
            if (!Applied) {
                Apply(declaringType, containingMethod, jumpSites);
            }
        }

        private static void CreateClosureParam(PatcherArguments arguments, TypeDefinition declaringType, string closureTypeName, ClosureCaptureData[] captures, out TypeDefinition closureTypeDef, out MethodDefinition closureConstructor, out VariableDefinition closure) {
            var module = arguments.MainModule;

            closureTypeDef = new TypeDefinition("", closureTypeName, TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) {
                DeclaringType = declaringType
            };
            foreach (var genericParam in declaringType.GenericParameters) { 
                closureTypeDef.GenericParameters.Add(genericParam.Clone());
            }

            var compilerGeneratedAttribute = new TypeReference("System.Runtime.CompilerServices", "CompilerGeneratedAttribute", module, module.TypeSystem.CoreLibrary);
            closureTypeDef.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor", module.TypeSystem.Void, compilerGeneratedAttribute) { HasThis = true }));

            closureConstructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            closureTypeDef.Methods.Add(closureConstructor);
            closureConstructor.Body = new MethodBody(closureConstructor);
            var insts = closureConstructor.Body.Instructions;
            insts.Add(Instruction.Create(OpCodes.Ldarg_0));
            insts.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            insts.Add(Instruction.Create(OpCodes.Ret));

            if (declaringType.HasGenericParameters) {
                foreach (var capture in captures) {
                    capture.CaptureField.FieldType = MonoModCommon.Structure.DeepMapTypeReference(
                        capture.CaptureField.FieldType, 
                        MonoModCommon.Structure.MapOption.Create(providers: [(declaringType, closureTypeDef)]));
                    closureTypeDef.Fields.Add(capture.CaptureField);
                }
            }
            else {
                closureTypeDef.Fields.AddRange(captures.Select(c => c.CaptureField));
            }
            TypeReference closureTypeRef = closureTypeDef;
            if (declaringType.HasGenericParameters) {
                var genericClosureTypeRef = new GenericInstanceType(closureTypeRef);
                foreach (var genericParam in declaringType.GenericParameters) {
                    genericClosureTypeRef.GenericArguments.Add(genericParam);
                }
                closureTypeRef = genericClosureTypeRef;
            }

            closure = new VariableDefinition(closureTypeRef);
        }

        public static ClosureData CreateClosureByCaptureThis(PatcherArguments arguments, TypeDefinition declaringType, MethodDefinition containingMethod, string closureTypeName) {
            ClosureCaptureData[] datas = [new ClosureCaptureData(containingMethod, containingMethod.Body.ThisParameter)];
            CreateClosureParam(arguments, declaringType, closureTypeName, datas, out TypeDefinition closureTypeDef, out MethodDefinition closureConstructor, out var closure);
            return new ClosureData(closureTypeDef, closureConstructor, closure, datas);
        }

        public static ClosureData CreateClosureByCaptureParam(PatcherArguments arguments, TypeDefinition declaringType, MethodDefinition containingMethod, string closureTypeName, ParameterDefinition parameter) {
            if (containingMethod.Body.ThisParameter == parameter) {
                return CreateClosureByCaptureThis(arguments, declaringType, containingMethod, closureTypeName);
            }
            ClosureCaptureData[] datas = [new ClosureCaptureData(containingMethod, parameter)];
            CreateClosureParam(arguments, declaringType, closureTypeName, datas, out TypeDefinition closureTypeDef, out MethodDefinition closureConstructor, out var closure);
            return new ClosureData(closureTypeDef, closureConstructor, closure, datas);
        }
        public static ClosureData CreateClosureByCaptureLocal(PatcherArguments arguments, TypeDefinition declaringType, MethodDefinition containingMethod, string closureTypeName, VariableDefinition local, string localName) {
            ClosureCaptureData[] datas = [new ClosureCaptureData(localName, local)];
            CreateClosureParam(arguments, declaringType, closureTypeName, datas, out TypeDefinition closureTypeDef, out MethodDefinition closureConstructor, out var closure);
            return new ClosureData(closureTypeDef, closureConstructor, closure, datas);
        }

        public static ClosureData CreateClosureByCaptureVariables(PatcherArguments arguments, TypeDefinition declaringType, MethodDefinition containingMethod, string closureTypeName, params ClosureCaptureData[] captures) {
            CreateClosureParam(arguments, declaringType, closureTypeName, captures, out TypeDefinition closureTypeDef, out MethodDefinition closureConstructor, out var closure);
            return new ClosureData(closureTypeDef, closureConstructor, closure, captures);
        }
        public static ClosureData CreateClosureDataFromExisting(TypeDefinition closureTypeDef, VariableDefinition closure, params ClosureCaptureData[] additionalCaptures) {
            return new ClosureData(
                closureTypeDef,
                closureTypeDef.Methods.Single(m => m.IsConstructor && !m.IsStatic),
                closure,
                additionalCaptures) {
                IsReusedClosure = true
            };
        }
    }
}
