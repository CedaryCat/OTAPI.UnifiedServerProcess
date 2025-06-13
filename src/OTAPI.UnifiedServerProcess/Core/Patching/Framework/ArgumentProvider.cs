namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public abstract class ArgumentProvider<TArgument> where TArgument : class
    {
        public abstract TArgument GenerateOrLoadCached();
        public abstract TArgument Generate();
    }
    public class ArgumentProvider<TSource, TResult>(ArgumentPipelineBuilder<TSource, TResult> builder, TSource raw) : ArgumentProvider<TResult>
        where TSource : Argument, IArgumentSource<TSource, TResult>
        where TResult : Argument
    {
        TResult? cached = default;
        bool isCached = false;
        public sealed override TResult GenerateOrLoadCached() {
            if (isCached) {
                return cached!;
            }
            cached = Generate();
            isCached = true;
            return cached!;
        }
        public sealed override TResult Generate() {
            return builder.Build(raw);
        }
    }
}
