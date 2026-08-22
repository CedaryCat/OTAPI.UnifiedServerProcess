using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Core.Patching;
using OTAPI.UnifiedServerProcess.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IInitializationDependencyCheckFeature : IJumpSitesCacheFeature, IMethodCheckCacheFeature
    {
    }

    public static class InitializationDependencyCheckFeatureExtensions
    {
        public static bool IsContextDependentInitialization(
            this IInitializationDependencyCheckFeature feature,
            IDictionary<string, FieldDefinition> contextualFields,
            MethodDefinition method,
            IEnumerable<Instruction> modificationOperations) {

            HashSet<Instruction> sources = [];
            HashSet<VariableDefinition> checkedLocals = [];
            var localPropagation = new InstructionSourceCollector.LocalPropagationOptions(checkedLocals.Add);

            InstructionSourceCollector.CollectSources(
                feature,
                method,
                sources,
                localPropagation,
                modificationOperations);

            Dictionary<Instruction, HashSet<Instruction>> branchBlockToConditions =
                InstructionSourceCollector.BuildBranchBlockToConditionsMap(method);

            bool incremented;
            do {
                int sourceCount = sources.Count;
                foreach (Instruction source in sources.ToArray()) {
                    if (!branchBlockToConditions.TryGetValue(source, out HashSet<Instruction>? conditions)) {
                        continue;
                    }
                    InstructionSourceCollector.CollectSources(
                        feature,
                        method,
                        sources,
                        localPropagation,
                        conditions);
                }
                incremented = sources.Count != sourceCount;
            }
            while (incremented);

            foreach (Instruction source in sources) {
                if (source.Operand is FieldReference fieldReference) {
                    if (fieldReference.FieldType.FullName == Constants.RootContextFullName
                        || contextualFields.ContainsKey(fieldReference.GetIdentifier())) {
                        return true;
                    }
                }

                if (source.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
                    || source.Operand is not MethodReference methodReference) {
                    continue;
                }

                MethodDefinition? calledMethod = methodReference.TryResolve();
                if (calledMethod is null) {
                    continue;
                }
                if ((calledMethod.Parameters.Count > 0
                        && calledMethod.Parameters[0].ParameterType.FullName == Constants.RootContextFullName)
                    || calledMethod.DeclaringType.Fields.Any(field => field.FieldType.FullName == Constants.RootContextFullName)
                    || feature.CheckUsedContextBoundField(contextualFields, calledMethod, useCache: false)) {
                    return true;
                }
            }

            return false;
        }
    }
}
