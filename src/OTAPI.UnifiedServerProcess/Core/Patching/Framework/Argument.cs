using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public class Argument
    {
        public TValue LoadVariable<TValue>() where TValue : new() {
            var index = VariableIndexMap.GetIndex<TValue>();
            while (index >= variables.Count) {
                variables.Add(null);
            }
            return (TValue)(variables[index] ??= new TValue());
        }
        public void StoreVariable<TValue>(TValue value) where TValue : new() {
            var index = VariableIndexMap.GetIndex<TValue>();
            while (index >= variables.Count) {
                variables.Add(null);
            }
            variables[index] = value;
        }
        readonly List<object?> variables = [];
        static class VariableIndexMap
        {
            static int count;
            class TypedVariableIndexMap<TValue>
            {
                public static readonly int index = count++;
            }
            public static int GetIndex<TValue>() {
                return TypedVariableIndexMap<TValue>.index;
            }
        }
    }
}
