using Mono.Cecil.Cil;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static OTAPI.UnifiedServerProcess.Commons.MonoModCommon.Stack;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class FlowPathExt
    {
        public static bool BeginAtSameInstruction<T>(this FlowPath<T>[] paths, [NotNullWhen(true)] out Instruction? begin) where T : ArgumentSource {
            begin = null;
            foreach (Instruction? first in paths.Select(p => p.ParametersSources[0].Instructions.First())) {
                begin ??= first;
                if (begin != first) {
                    return false;
                }
            }
            if (begin is null) throw new InvalidOperationException("No paths provided.");
            return true;
        }
    }
}
