using Mono.Cecil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// After contextualization and cleanup, property accessors with added RootContext parameters should be decomposed into standalone methods since they no longer qualify as property members.
    /// <para>Additionally, properties/accessors partially removed during cleanup require full removal.</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public class AdjustPropertiesPatcher(ILogger logger, MethodCallGraph callGraph) : GeneralPatcher(logger)
    {
        public override string Name => nameof(AdjustPropertiesPatcher);

        public override void Patch(PatcherArguments arguments) {
            var mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();
            foreach (var type in arguments.MainModule.GetAllTypes()) {
                foreach (var prop in type.Properties.ToArray()) {
                    string getterName = "get_" + prop.Name;
                    string setterName = "set_" + prop.Name;

                    var nameParts = prop.Name.Split('.');
                    if (nameParts.Length > 1) {
                        var propName = nameParts[^1];
                        nameParts[^1] = "get_" + propName;
                        getterName = string.Join('.', nameParts);

                        nameParts[^1] = "set_" + propName;
                        setterName = string.Join('.', nameParts);
                    }

                    var getter = type.Methods.FirstOrDefault(x => x.Name == getterName);
                    var setter = type.Methods.FirstOrDefault(x => x.Name == setterName);

                    var innerField = type.Fields.FirstOrDefault(x => x.Name == $"<{prop.Name}>k__BackingField");

                    bool shouldRemove = getter is null && setter is null;

                    if (shouldRemove && innerField is not null) {
                        type.Fields.Remove(innerField);
                        // keep the original declaring type, because it might be used later
                        innerField.DeclaringType = type;
                    }
                    if (shouldRemove) {
                        type.Properties.Remove(prop);
                        // keep the original declaring type, because it might be used later
                        prop.DeclaringType = type;
                    }
                }
                foreach (var method in type.Methods.ToArray()) {
                    if (!method.IsSpecialName) {
                        continue;
                    }
                    bool isGetter = method.Name.OrdinalStartsWith("get_");
                    bool isSetter = method.Name.OrdinalStartsWith("set_");
                    if (!isGetter && !isSetter) {
                        continue;
                    }

                    var propName = method.Name[4..];
                    var prop = type.Properties.FirstOrDefault(x => x.Name == propName);

                    if (arguments.ContextTypes.TryGetValue(type.FullName, out var contextTypeData) 
                        && contextTypeData.originalType.FullName != type.FullName
                        && contextTypeData.originalType.Module == type.Module) {

                        if (prop is null) {
                            prop = new PropertyDefinition(propName, PropertyAttributes.None, isGetter ? method.ReturnType : method.Parameters[0].ParameterType);
                            type.Properties.Add(prop);
                        }

                        var originalSetter = contextTypeData.originalType.Methods.FirstOrDefault(x => x.Name == $"set_{propName}");
                        var originalGetter = contextTypeData.originalType.Methods.FirstOrDefault(x => x.Name == $"get_{propName}");
                        var originalProp = contextTypeData.originalType.Properties.FirstOrDefault(x => x.Name == propName);

                        if (isSetter && prop.SetMethod is null) {
                            prop.SetMethod = method;
                            if (originalProp is not null) {
                                originalProp.SetMethod = null;
                            }
                        }
                        else if (isGetter && prop.GetMethod is null) {
                            prop.GetMethod = method;
                            if (originalProp is not null) {
                                originalProp.SetMethod = null;
                            }
                        }

                        if (originalProp is not null && originalProp.GetMethod is null && originalProp.SetMethod is null) {
                            contextTypeData.originalType.Properties.Remove(originalProp);
                        }
                    }
                    else {
                        if (prop is null) {
                            Warn("Property {0} not found in type {1}", propName, type.FullName);
                            method.IsSpecialName = false;
                        }
                        else {
                            CheckAndRemoveProperty(arguments, mappedMethods, type, prop);
                        }
                    }
                }
            }
        }

        private bool CheckAndRemoveProperty(PatcherArguments arguments, ContextBoundMethodMap mappedMethods, TypeDefinition type, PropertyDefinition prop) {

            if ((prop.SetMethod is not null && prop.SetMethod.Parameters.Count == 2 && prop.SetMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName)
                || (prop.GetMethod is not null && prop.GetMethod.Parameters.Count == 1 && prop.GetMethod.Parameters[0].ParameterType.FullName == arguments.RootContextDef.FullName)) {

                if (prop.GetMethod is not null) {
                    TransformAccessor(arguments, mappedMethods, type, prop.GetMethod, prop);
                }
                if (prop.SetMethod is not null) {
                    TransformAccessor(arguments, mappedMethods, type, prop.SetMethod, prop);
                }

                type.Properties.Remove(prop);
                // keep the original declaring type, because it might be used later
                prop.DeclaringType = type;
                return true;
            }

            return false;
        }
        private void TransformAccessor(PatcherArguments arguments, ContextBoundMethodMap mappedMethods, TypeDefinition type, MethodDefinition accessor, PropertyDefinition prop) {
            bool isGetter = accessor.Name.OrdinalStartsWith("get_");

            string newMethodName = isGetter ? $"Get{prop.Name}" : $"Set{prop.Name}";

            var accessorOrigId = PatchingCommon.GetVanillaMethodRef(arguments.RootContextDef, arguments.ContextTypes, accessor).GetIdentifier();
            var accessorId = accessor.GetIdentifier();

            if (callGraph.MediatedCallGraph.TryGetValue(accessorOrigId, out var callData)) {
                foreach (var callerItem in callData.UsedByMethods) {
                    var caller = callerItem;

                    if (mappedMethods.originalToContextBound.TryGetValue(callerItem.GetIdentifier(), out var convertedMethod)) {
                        caller = convertedMethod;
                    }

                    foreach (var il in caller.Body.Instructions) {
                        if (il.Operand is MethodReference accessorRef && accessorRef.GetIdentifier() == accessorId) {
                            accessorRef.Name = newMethodName;
                        }
                    }
                }
            }

            accessor.Name = newMethodName;
            accessor.IsSpecialName = false;
        }
    }
}
