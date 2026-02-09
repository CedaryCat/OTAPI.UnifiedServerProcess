using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis
{
    public sealed class ParameterUsageTrack(
        MethodDefinition methodDefinition,
        AggregatedParameterProvenance? returnValueTrace,
        ParameterTraceCollection<string> parameterTraces,
        ParameterTraceCollection<VariableDefinition> localVariableTraces,
        ParameterTraceCollection<string> stackValueTraces)
    {
        public MethodDefinition MethodDefinition { get; } = methodDefinition ?? throw new ArgumentNullException(nameof(methodDefinition));
        public AggregatedParameterProvenance? ReturnValueTrace { get; } = returnValueTrace;
        public ParameterTraceCollection<string> ParameterTraces { get; } = parameterTraces ?? new ParameterTraceCollection<string>();
        public ParameterTraceCollection<VariableDefinition> LocalVariableTraces { get; } = localVariableTraces ?? new ParameterTraceCollection<VariableDefinition>();
        public ParameterTraceCollection<string> StackValueTraces { get; } = stackValueTraces ?? new ParameterTraceCollection<string>();

        public static string GenerateStackKey(MethodDefinition method, Instruction instruction) {
            if (instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld) {
                FieldReference fieldRef = (FieldReference)instruction.Operand;
                return $"Field#{fieldRef.DeclaringType.FullName}→{fieldRef.Name}";
            }

            if (MonoModCommon.IL.TryGetReferencedParameter(method, instruction, out var parameter)) {
                return $"Param#{method.GetIdentifier()}→{parameter.GetDebugName()}";
            }

            if (method.HasBody && MonoModCommon.IL.TryGetReferencedVariable(method, instruction, out var variable)) {
                return $"Variable#{method.GetIdentifier()}→V_{variable.Index}";
            }

            return $"Others#{method.GetIdentifier()}→IL_{instruction.Offset:X4}";
        }
    }
}
