using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments
{
    public class NewConstraintInjectCtxProcessor(AnalyzerGroups analyzers) : IGeneralArgProcessor, IMethodCheckCacheFeature
    {
        public MethodCallGraph MethodCallGraph => analyzers.MethodCallGraph;

        public void Apply(LoggedComponent logger, ref PatcherArgumentSource source) {
            
            foreach (var typeDef in source.MainModule.GetAllTypes()) {
                foreach(var methodDef in typeDef.Methods) {
                    if (!methodDef.HasBody) {
                        continue;
                    }
                    foreach (var inst in methodDef.Body.Instructions) {
                        if (inst.Operand is not GenericInstanceMethod { ElementMethod.DeclaringType.FullName: $"{nameof(System)}.{nameof(Activator)}" } gim) {
                            continue;
                        }
                        if (gim.GenericArguments.Single() is not GenericParameter gp) {
                            continue;
                        }
                        var typeConstr = gp.Constraints
                            .Select(c => c.ConstraintType?.Resolve())
                            .FirstOrDefault(x => x is not null && !x.IsInterface && !x.IsValueType);
                        if (typeConstr is null) {
                            continue;
                        }
                        var inheritances = analyzers.TypeInheritanceGraph.GetDerivedTypeTree(typeConstr);
                        bool ctxBound = false;
                        foreach (var inheritance in inheritances) {
                            var childCtor = inheritance.Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count is 0);
                            if (this.CheckUsedContextBoundField(source.OriginalToInstanceConvdField, childCtor)) {
                                ctxBound = true;
                                break;
                            }
                        }
                        if (!ctxBound) {
                            continue;
                        }

                        gp.Attributes &= ~GenericParameterAttributes.DefaultConstructorConstraint;

                        if (source.NewConstraintInjectedCtx.TryAdd(typeConstr.FullName, typeConstr)) {
                            if (typeConstr.Fields.Any(x => x.Name is Constants.RootContextFieldName)) {
                                throw new NotSupportedException();
                            }
                            var field = new FieldDefinition(Constants.RootContextFieldName, FieldAttributes.Public, source.RootContextDef);
                            typeConstr.Fields.Add(field);

                            var ctor = typeConstr.Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count is 0);
                            var callbaseCtor = ctor.Body.Instructions.Single(x => x is { OpCode.Code: Code.Call, Operand: MethodReference { Name: ".ctor" } });

                            var il = ctor.Body.GetILProcessor();
                            il.InsertAfter(callbaseCtor, [
                                Instruction.Create(OpCodes.Ldarg_0),
                                Instruction.Create(OpCodes.Ldarg_1),
                                Instruction.Create(OpCodes.Stfld, field),
                            ]);

                            foreach (var inheritance in inheritances) {
                                source.RootContextFieldToAdaptExternalInterface.Add(inheritance.FullName, field);

                                var childCtor = inheritance.Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count is 0);
                                childCtor.Parameters.Insert(0, new ParameterDefinition(Constants.RootContextFieldName, ParameterAttributes.None, source.RootContextDef));
                            }
                        }
                    }
                }
            }
        }
    }
}
