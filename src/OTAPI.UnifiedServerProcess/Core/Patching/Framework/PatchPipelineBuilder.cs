using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public abstract class PatchPipelineBuilder(ILogger logger) : LoggedComponent(logger)
    {
        private readonly ILogger logger = logger;
        public abstract void Execute();
        public abstract string Print();
        /// <summary>
        /// Defines an argument in patching fluent
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="source">It must type of <see cref="TSource"/></param>
        /// <returns></returns>
        public ArgumentConfigurator<TSource, TResult> DefineArgument<TSource, TResult>(IArgumentSource<TSource, TResult> source)
            where TSource : Argument, IArgumentSource<TSource, TResult>
            where TResult : Argument {

            var result = new ArgumentConfigurator<TSource, TResult>(logger, this, (TSource)source);
            return result;
        }
    }
}
