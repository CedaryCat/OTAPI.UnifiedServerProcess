using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    /// <summary>
    /// Removes unusable compiler-generated artifacts left after contextualization and MonoMod patching.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="module"></param>
    public class RemoveUnusedCodePatcherAtEnd(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(RemoveUnusedCodePatcherAtEnd);

        public override void Patch() {

            RemoveOrphanedIteratorShells(module);
            RemoveUnusedCompilerGeneratedOriginals(module);

            //var legarcyLighting = module.GetType("Terraria.Graphics.Light.LegacyLighting");
            //var legarcyLighting_ctor = legarcyLighting.Methods.Single(m => m.Name == ".ctor");

            //legarcyLighting_ctor.Body.Variables.Clear();
            //legarcyLighting_ctor.Body.Instructions.Clear();
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, new FieldReference(Constants.RootContextFieldName, rootDef, legarcyLighting)));
            //legarcyLighting_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            //var lightSysCxt = module.GetType("Terraria.LightingSystemContext");
            //var lightSysCxt_ctor = lightSysCxt.Methods.Single(m => m.Name == ".ctor");

            //lightSysCxt_ctor.Body.Variables.Clear();
            //lightSysCxt_ctor.Body.Instructions.Clear();
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, new FieldReference(Constants.RootContextFieldName, rootDef, lightSysCxt)));
            //lightSysCxt_ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        static void RemoveOrphanedIteratorShells(ModuleDefinition module) {
            foreach (TypeDefinition type in module.GetAllTypes().ToArray()) {
                if (type.DeclaringType is null
                    || !type.Name.Contains(">d__", StringComparison.Ordinal)
                    || !type.Interfaces.Any(@interface => @interface.InterfaceType.FullName == "System.Collections.IEnumerator")
                    || type.Methods.Count == 0
                    || type.Methods.Any(method => !method.IsConstructor)) {
                    continue;
                }

                // EnumeratorCtxAdaptPatcher clones context-dependent state machines under the
                // generated SystemContext type. CleanupCtxUnboundPatcher then removes every
                // mapped method from the old state machine, leaving an invalid interface shell.
                TypeDefinition declaringType = type.DeclaringType;
                declaringType.NestedTypes.Remove(type);
                type.DeclaringType = declaringType;
            }
        }

        static void RemoveUnusedCompilerGeneratedOriginals(ModuleDefinition module) {
            var referencedMethods = new HashSet<string>(StringComparer.Ordinal);
            TypeDefinition[] types = module.GetAllTypes().ToArray();

            foreach (TypeDefinition type in types) {
                foreach (PropertyDefinition property in type.Properties) {
                    AddMethod(property.GetMethod);
                    AddMethod(property.SetMethod);
                    foreach (MethodDefinition otherMethod in property.OtherMethods) {
                        AddMethod(otherMethod);
                    }
                }
                foreach (EventDefinition eventDefinition in type.Events) {
                    AddMethod(eventDefinition.AddMethod);
                    AddMethod(eventDefinition.RemoveMethod);
                    AddMethod(eventDefinition.InvokeMethod);
                    foreach (MethodDefinition otherMethod in eventDefinition.OtherMethods) {
                        AddMethod(otherMethod);
                    }
                }
                foreach (MethodDefinition method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (var instruction in method.Body.Instructions) {
                        if (instruction.Operand is not MethodReference methodReference) {
                            continue;
                        }
                        referencedMethods.Add(methodReference.FullName);
                        if (!methodReference.Name.OrdinalStartsWith("orig_")) {
                            continue;
                        }
                        try {
                            AddMethod(methodReference.Resolve());
                        }
                        catch (AssemblyResolutionException) {
                            // External references do not identify a removable local method.
                        }
                    }
                }
            }

            foreach (TypeDefinition type in types) {
                if (!type.Name.OrdinalStartsWith("<")) {
                    continue;
                }

                foreach (MethodDefinition method in type.Methods.ToArray()) {
                    if (!method.Name.OrdinalStartsWith("orig_")) {
                        continue;
                    }

                    if (!referencedMethods.Contains(method.FullName)
                        && (method.IsPrivate || !IsExternallyVisible(type))) {
                        type.Methods.Remove(method);
                        method.DeclaringType = type;
                        continue;
                    }

                    // MonoMod's original clone is non-virtual. Retaining MethodImpl rows on
                    // such a clone violates the CLR interface contract even when the body is
                    // still needed by a direct call.
                    if (!method.IsVirtual && method.Overrides.Count != 0) {
                        method.Overrides.Clear();
                    }
                }
            }

            void AddMethod(MethodReference? method) {
                if (method is not null) {
                    referencedMethods.Add(method.FullName);
                }
            }

            static bool IsExternallyVisible(TypeDefinition type) {
                if (type.DeclaringType is null) {
                    return type.IsPublic;
                }

                bool nestedVisibility = type.IsNestedPublic
                    || type.IsNestedFamily
                    || type.IsNestedFamilyOrAssembly;
                return nestedVisibility && IsExternallyVisible(type.DeclaringType);
            }
        }
    }
}
