using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
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
            var threadLocalInitalizer = module.GetType("UnifiedServerProcess.ThreadLocalInitializer");
            var method = threadLocalInitalizer.GetMethod("Initialize");

            method.Body.Instructions.Clear();
            var insts = method.Body.Instructions;

            foreach (var type in module.GetAllTypes()) {
                foreach(var field in type.Fields) {
                    if (field.IsStatic && 
                        field.CustomAttributes.Any(x => x.AttributeType.Name is nameof(ThreadStaticAttribute)) && 
                        !field.FieldType.IsValueType) {
                        var ft = field.FieldType.TryResolve();
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
