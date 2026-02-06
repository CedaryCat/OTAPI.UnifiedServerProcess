using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.DataModels
{
    public class ContextBoundMethodMap
    {
        /// <summary>
        ///  Key: original method identifier, Value: converted method
        /// </summary>
        public DebugMap originalToContextBound = [];
        /// <summary>
        /// Only the context-bound methods | Key: converted method identifier, Value: converted method
        /// </summary>
        public DebugMap contextBoundMethods = [];
        public class DebugMap : Dictionary<string, MethodDefinition>
        {
            public new bool TryAdd(string key, MethodDefinition m) {
                return base.TryAdd(key, m);
            }
            public new bool Remove(string key) {
                return base.Remove(key);
            }
            public new void Add(string key, MethodDefinition m) {
                base.Add(key, m);
            }
        }
    }
}
