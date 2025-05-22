using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching {
    /// <summary>
    /// Removes all original counterparts of contextualized methods and fields after the completion of primary contextualization logic.
    /// </summary>
    /// <param name="logger"></param>
    public class CleanupCtxUnboundPatcher(ILogger logger) : GeneralPatcher(logger) {
        public override string Name => nameof(CleanupCtxUnboundPatcher);

        public override void Patch(PatcherArguments arguments) {
            var mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();
            foreach (var type in arguments.MainModule.GetAllTypes()) {

                foreach (var field in type.Fields.Where(f => !f.Name.EndsWith(Constants.Patching.ConvertedFieldInSingletonSuffix)).ToArray()) {
                    if (arguments.InstanceConvdFieldOrgiMap.TryGetValue(field.FullName, out var newField)) {
                        type.Fields.Remove(field);
                        // keep the original declaring type, because it might be used later
                        field.DeclaringType = type;
                        if (newField.DeclaringType.FullName == arguments.RootContextDef.FullName) {
                            continue;
                        }
                        var contextType = arguments.ContextTypes[newField.DeclaringType.FullName];
                        if (contextType.IsReusedSingleton && !contextType.ReusedSingletonFields.ContainsKey(field.FullName)) {
                            newField.Name = field.Name;
                        }
                    }
                }

                arguments.ContextTypes.TryGetValue(type.FullName, out var contextBoundType);
                if (contextBoundType?.IsReusedSingleton ?? false) {
                    continue;
                }

                foreach (var method in type.Methods.ToArray()) {
                    if (method.IsConstructor) {
                        continue;
                    }
                    if (mappedMethods.originalToContextBound.ContainsKey(method.GetIdentifier())) {
                        type.Methods.Remove(method);
                        // keep the original declaring type, because it might be used later
                        method.DeclaringType = type;
                    }
                }
            }
        }
    }
}
