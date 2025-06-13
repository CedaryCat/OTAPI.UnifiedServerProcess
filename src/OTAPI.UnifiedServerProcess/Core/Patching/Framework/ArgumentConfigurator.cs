using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public class ArgumentConfigurator<TSource, TResult>(ILogger logger, PatchPipelineBuilder owner, TSource raw)
        where TSource : Argument, IArgumentSource<TSource, TResult>
        where TResult : Argument
    {

        readonly ArgumentPipelineBuilder<TSource, TResult> argumentBuilder = new(logger);

        public ArgumentConfigurator<TSource, TResult> RegisterProcessor(IArgumentBuildProcessor<TSource> processor) {
            argumentBuilder.AddProcessor(processor);
            return this;
        }
        public PatchingChain<TResult> ApplyArgument() {
            return new PatchingChain<TResult>(logger, new ArgumentProvider<TSource, TResult>(argumentBuilder, raw), owner);
        }
    }
}
