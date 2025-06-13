using Mono.Cecil;
using System;
using System.Linq.Expressions;

namespace OTAPI.UnifiedServerProcess.Commons
{
    public static partial class MonoModCommon
    {
        public static class Reference
        {
            [MonoMod.MonoModIgnore]
            public static System.Reflection.MethodBase Method(Expression expression) {
                if (expression is LambdaExpression lambdaExp) {
                    if (lambdaExp.Body is MethodCallExpression methodCallExp) {
                        return methodCallExp.Method;
                    }
                    if (lambdaExp.Body is NewExpression newExp) {
                        return newExp.Constructor ?? throw new InvalidOperationException("No constructor found in new expression.");
                    }
                    else if (
                        lambdaExp.Body is UnaryExpression unaryExp &&
                        unaryExp.Operand is MethodCallExpression createDeleMethodCallExp &&
                        createDeleMethodCallExp.Object is ConstantExpression constantExp &&
                        constantExp.Value is System.Reflection.MethodInfo info) {
                        return info;
                    }
                }
                throw new InvalidOperationException("No method call found in lambda expression.");
            }
            public static MethodReference ImportMethod(ModuleDefinition module, Expression expression) => module.ImportReference(Method(expression));
        }
    }
}
