using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class ThreadLocalInitPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(ThreadLocalInitPatcher);
        public override void Patch() {
            TypeDefinition threadLocalInitalizer = module.GetType("UnifiedServerProcess.ThreadLocalInitializer");
            MethodDefinition method = threadLocalInitalizer.GetMethod("Initialize");

            method.Body.Instructions.Clear();
            Collection<Instruction> insts = method.Body.Instructions;

            foreach (TypeDefinition? type in module.GetAllTypes()) {
                foreach (FieldDefinition? field in type.Fields) {
                    if (field.IsStatic &&
                        field.CustomAttributes.Any(x => x.AttributeType.Name is nameof(ThreadStaticAttribute)) &&
                        !field.FieldType.IsValueType) {
                        TypeDefinition? ft = field.FieldType.TryResolve();
                        if (ft is not null && ft.Methods.Any(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count is 0)) {
                            insts.Add(Instruction.Create(OpCodes.Newobj, new MethodReference(".ctor", module.TypeSystem.Void, field.FieldType) { HasThis = true, }));
                            insts.Add(Instruction.Create(OpCodes.Stsfld, field));

                            if (field.IsPrivate) {
                                field.IsAssembly = true;
                            }
                        }
                    }
                }
            }

            insts.Add(Instruction.Create(OpCodes.Ret));
        }
    }
}
