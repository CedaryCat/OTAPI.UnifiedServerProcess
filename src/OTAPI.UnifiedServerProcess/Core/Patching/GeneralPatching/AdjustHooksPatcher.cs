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
            TypeDefinition hookEventDelegate = arguments.MainModule.GetType("HookEvents.HookDelegate")
                ?? throw new Exception("HookEvents.HookDelegate is not found.");
            ContextBoundMethodMap mappedMethods = arguments.LoadVariable<ContextBoundMethodMap>();

            foreach (TypeDefinition? type in arguments.MainModule.GetAllTypes()) {
                if (!type.GetRootDeclaringType().Namespace.OrdinalStartsWith("HookEvents.")) {
                    continue;
                }
                if (type.BaseType?.Name == "MulticastDelegate") {
                    continue;
                }
                foreach (MethodDefinition? invokeMethod in type.Methods) {
                    if (!invokeMethod.Name.OrdinalStartsWith("Invoke")) {
                        continue;
                    }
                    var methodId = invokeMethod.GetIdentifier();
                    if (!callGraph.MediatedCallGraph.TryGetValue(methodId, out MethodCallData? callData)) {
                        continue;
                    }
                    MethodDefinition containingMethod = callData.UsedByMethods.Single();
                    if (mappedMethods.originalToContextBound.TryGetValue(containingMethod.GetIdentifier(), out MethodDefinition? convertedMethod)) {
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

            foreach (Instruction? inst in containingMethod.Body.Instructions) {
                if (inst.OpCode != OpCodes.Newobj) {
                    continue;
                }
                MethodDefinition ctor = ((MethodReference)inst.Operand).Resolve();
                if (ctor?.DeclaringType.BaseType.Name != "MulticastDelegate") {
                    continue;
                }
                TypeDefinition delegateDef = ctor.DeclaringType;

                MethodDefinition invokeDef = delegateDef.GetMethod("Invoke");
                MethodDefinition beginInvoke = delegateDef.GetMethod("BeginInvoke");
                for (int i = 0; i < containingMethod.Parameters.Count; i++) {
                    invokeDef.Parameters[i].Attributes = containingMethod.Parameters[i].Attributes;
                    beginInvoke.Parameters[i].Attributes = containingMethod.Parameters[i].Attributes;
                }
            }
        }
    }
}
