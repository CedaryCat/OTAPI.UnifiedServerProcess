using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.Framework;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.SimplePatching
{
    public class AdjustAutoPropertiesPatcher(ILogger logger, ModuleDefinition module) : Patcher(logger)
    {
        public override string Name => nameof(AdjustAutoPropertiesPatcher);

        public override void Patch() {

            Dictionary<string, string> nameMap = [];

            foreach (TypeDefinition? type in module.GetAllTypes()) {
                foreach (MethodDefinition? method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }

                    if (!method.CustomAttributes.Any(ca => ca.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")) {
                        continue;
                    }

                    if (!method.Name.OrdinalStartsWith("get_") && !method.Name.OrdinalStartsWith("set_")) {
                        continue;
                    }

                    if (!method.Body.Instructions.Any(inst => inst.Operand is MethodReference mr && mr.Name.OrdinalStartsWith("mfwh_"))) {
                        continue;
                    }

                    var propertyName = method.Name.Substring(4);

                    FieldDefinition? autoField = type.Fields.FirstOrDefault(f => f.Name == string.Concat("<", propertyName, ">k__BackingField"));
                    if (autoField == null) {
                        continue;
                    }

                    var newName = string.Concat("__", propertyName);
                    nameMap.Add(autoField.GetIdentifier(), newName);
                    autoField.Name = newName;
                }
            }
            foreach (TypeDefinition? type in module.GetAllTypes()) {
                foreach (MethodDefinition? method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }

                    foreach (Instruction? inst in method.Body.Instructions) {
                        if (inst.Operand is FieldReference fr && nameMap.TryGetValue(fr.GetIdentifier(), out string? value)) {
                            fr.Name = value;
                        }
                    }
                }
            }
        }
    }
}
