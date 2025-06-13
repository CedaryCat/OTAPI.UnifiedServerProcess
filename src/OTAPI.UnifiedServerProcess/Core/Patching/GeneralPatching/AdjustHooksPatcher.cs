using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Patching.DataModels;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// Fix the problem that the OriginalDelegate of HookEvents does not have the same ParameterAttributes as the corresponding method
    /// <para>It is an optional step that is not a necessary step for contextualization</para>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="callGraph"></param>
    public class AdjustHooksPatcher(ILogger logger, MethodCallGraph callGraph) : GeneralPatcher(logger)
    {
        public override string Name => nameof(AdjustHooksPatcher);

        public override void Patch(PatcherArguments arguments) {
            var hookEventDelegate = arguments.MainModule.GetType("HookEvents.HookDelegate")
                ?? throw new Exception("HookEvents.HookDelegate is not found.");
            var mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();

            foreach (var type in arguments.MainModule.GetAllTypes()) {
                if (!type.GetRootDeclaringType().Namespace.StartsWith("HookEvents.")) {
                    continue;
                }
                if (type.BaseType?.Name == "MulticastDelegate") {
                    continue;
                }
                foreach (var invokeMethod in type.Methods) {
                    if (!invokeMethod.Name.StartsWith("Invoke")) {
                        continue;
                    }
                    var methodId = invokeMethod.GetIdentifier();
                    if (!callGraph.MediatedCallGraph.TryGetValue(methodId, out var callData)) {
                        continue;
                    }
                    var containingMethod = callData.UsedByMethods.Single();
                    if (mappedMethods.originalToContextBound.TryGetValue(containingMethod.GetIdentifier(), out var convertedMethod)) {
                        containingMethod = convertedMethod;
                    }

                    ProcessMethod(containingMethod);
                }
            }
        }

        private static void ProcessMethod(MethodDefinition containingMethod) {
            if (containingMethod.Parameters.All(p => p.Attributes is ParameterAttributes.None)) {
                return;
            }

            foreach (var inst in containingMethod.Body.Instructions) {
                if (inst.OpCode != OpCodes.Newobj) {
                    continue;
                }
                var ctor = ((MethodReference)inst.Operand).Resolve();
                if (ctor?.DeclaringType.BaseType.Name != "MulticastDelegate") {
                    continue;
                }
                var delegateDef = ctor.DeclaringType;

                var invokeDef = delegateDef.GetMethod("Invoke");
                var beginInvoke = delegateDef.GetMethod("BeginInvoke");
                for (int i = 0; i < containingMethod.Parameters.Count; i++) {
                    invokeDef.Parameters[i].Attributes = containingMethod.Parameters[i].Attributes;
                    beginInvoke.Parameters[i].Attributes = containingMethod.Parameters[i].Attributes;
                }
            }
        }
    }
}
