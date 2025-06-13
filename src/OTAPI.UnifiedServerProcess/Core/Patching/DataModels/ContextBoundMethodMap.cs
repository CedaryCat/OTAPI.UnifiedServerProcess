using Mono.Cecil;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.Patching.DataModels
{
    public class ContextBoundMethodMap
    {
        /// <summary>
        ///  Key: original method identifier, Value: converted method
        /// </summary>
        public Dictionary<string, MethodDefinition> originalToContextBound = [];
        /// <summary>
        /// Only the context-bound methods | Key: converted method identifier, Value: converted method
        /// </summary>
        public Dictionary<string, MethodDefinition> contextBoundMethods = [];
    }
}
