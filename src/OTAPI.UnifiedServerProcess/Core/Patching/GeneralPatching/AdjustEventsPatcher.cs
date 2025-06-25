using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Similar to <see cref="AdjustPropertiesPatcher"/>, events also require corresponding contextualization adaptations.
    /// </summary>
    /// <param name="logger"></param>
    public class AdjustEventsPatcher(ILogger logger) : GeneralPatcher(logger)
    {
        public override string Name => nameof(AdjustPropertiesPatcher);

        public override void Patch(PatcherArguments arguments) {
            foreach (var type in arguments.MainModule.GetAllTypes()) {
                foreach (var theEvent in type.Events.ToArray()) {
                    var adder = type.Methods.FirstOrDefault(x => x.Name == "add_" + theEvent.Name);
                    var remover = type.Methods.FirstOrDefault(x => x.Name == "remove_" + theEvent.Name);
                    var innerField = type.Fields.FirstOrDefault(x => x.Name == theEvent.Name && x.FieldType.FullName == theEvent.EventType.FullName);

                    bool shouldRemove = adder is null && remover is null;

                    if (shouldRemove && innerField is not null) {
                        type.Fields.Remove(innerField);
                        // keep the original declaring type, because it might be used later
                        innerField.DeclaringType = type;
                    }
                    if (shouldRemove) {
                        type.Events.Remove(theEvent);
                        // keep the original declaring type, because it might be used later
                        theEvent.DeclaringType = type;
                    }
                }
                foreach (var method in type.Methods) {
                    if (!method.IsSpecialName) {
                        continue;
                    }

                    string theEventName;
                    bool isAdder = method.Name.OrdinalStartsWith("add_");
                    bool isRemover = method.Name.OrdinalStartsWith("remove_");
                    if (!isAdder && !isRemover) {
                        continue;
                    }

                    if (isAdder) {
                        theEventName = method.Name["add_".Length..];
                    }
                    else {
                        theEventName = method.Name["remove_".Length..];
                    }

                    var theEvent = type.Events.FirstOrDefault(x => x.Name == theEventName);

                    var innerField = type.Fields.FirstOrDefault(x => x.Name == theEventName);
                    if (innerField is not null) {
                        var nullableAtt = innerField.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
                        if (nullableAtt is not null) {
                            innerField.CustomAttributes.Remove(nullableAtt);
                        }
                    }

                    if (arguments.ContextTypes.ContainsKey(type.FullName)) {
                        if (theEvent is null) {
                            theEvent = new EventDefinition(theEventName, EventAttributes.None, method.Parameters[0].ParameterType);
                            type.Events.Add(theEvent);
                        }
                        if (isRemover && theEvent.RemoveMethod is null) {
                            theEvent.RemoveMethod = method;
                        }
                        else if (isAdder && theEvent.AddMethod is null) {
                            theEvent.AddMethod = method;
                        }
                    }
                    else {
                        if (theEvent is null) {
                            Warn("Event {0} not found in type {1}", theEventName, type.FullName);
                            method.IsSpecialName = false;
                        }
                        else {
                            CheckEvent(arguments, type, theEvent);
                        }
                    }
                }
            }
        }

        static bool CheckEvent(PatcherArguments arguments, TypeDefinition type, EventDefinition theEvent) {

            if ((theEvent.RemoveMethod is null || theEvent.RemoveMethod.Parameters.Count == 1)
                && (theEvent.AddMethod is null || theEvent.AddMethod.Parameters.Count == 1)) {
                return false;
            }
            throw new Exception($"Unexpected event {theEvent.Name} in type {type.FullName}");
        }
    }
}
