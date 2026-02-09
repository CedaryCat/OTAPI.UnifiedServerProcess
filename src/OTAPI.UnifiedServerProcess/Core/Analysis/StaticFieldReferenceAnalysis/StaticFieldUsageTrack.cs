using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis
{
    public class StaticFieldUsageTrack(
        MethodDefinition methodDefinition,
        AggregatedStaticFieldProvenance? returnValueTrace,
        StaticFieldTraceCollection<string> parameterTraces,
        StaticFieldTraceCollection<VariableDefinition> localVariableTraces,
        StaticFieldTraceCollection<string> stackValueTraces)
    {
        public MethodDefinition MethodDefinition { get; } = methodDefinition ?? throw new ArgumentNullException(nameof(methodDefinition));
        public AggregatedStaticFieldProvenance? ReturnValueTrace { get; } = returnValueTrace;
        public StaticFieldTraceCollection<string> StaticFieldTraces { get; } = parameterTraces ?? new StaticFieldTraceCollection<string>();
        public StaticFieldTraceCollection<VariableDefinition> LocalVariableTraces { get; } = localVariableTraces ?? new StaticFieldTraceCollection<VariableDefinition>();
        public StaticFieldTraceCollection<string> StackValueTraces { get; } = stackValueTraces ?? new StaticFieldTraceCollection<string>();

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
