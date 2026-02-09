using Microsoft.CodeAnalysis;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;
using System.Linq;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public class ArgumentPipelineBuilder<TSource, TResult> : LoggedComponent
        where TSource : Argument, IArgumentSource<TSource, TResult>
        where TResult : Argument
    {
        public ArgumentPipelineBuilder(ILogger logger) : base(logger) {
            currentName = GetType().Name;
        }
        readonly List<IArgumentBuildProcessor<TSource>> processors = [];
        string currentName;
        public sealed override string Name => currentName;
        public TResult Build(TSource args) {

            foreach (IArgumentBuildProcessor<TSource> processor in processors) {
                currentName = processor.GetType().Name;
                processor.Apply(this, ref args);
            }

            return args.Build();
        }
        public ArgumentPipelineBuilder<TSource, TResult> AddProcessor(IArgumentBuildProcessor<TSource> processor) {
            Info($"Adding processor: {processor.GetType().Name}");
            processors.Add(processor);
            return this;
        }
        public override string ToString() => $"[AgumentBuilder:{string.Join(",", processors.Select(x => x.GetType().Name))}]";
    }
}
