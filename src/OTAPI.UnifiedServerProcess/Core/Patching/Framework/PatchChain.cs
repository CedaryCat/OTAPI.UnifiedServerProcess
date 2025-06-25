using OTAPI.UnifiedServerProcess.Loggers;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public class PatchChain(ILogger logger, PatchPipelineBuilder? previous = null) : PatchPipelineBuilder(logger)
    {
        private readonly PatchPipelineBuilder? previous = previous;
        private readonly ILogger logger = logger;
        public override string Name => nameof(PatchChain);
        public override void Execute() {
            Info("Starting patching tail");
            previous?.Execute();
        }
        public override string Print() {
            if (previous is not null) {
                return previous.Print();
            }
            return "[Empty]";
        }
        public override string ToString() => Print();
        public PatchChain Then(Patcher next) {
            Info($"Adding patcher: {next.Name}");
            return new LinkedPatchChain(logger, this, new SinglePatchProcessor(logger, next));
        }
        class SinglePatchProcessor(ILogger logger, Patcher patcher) : PatchChain(logger)
        {
            private readonly Patcher patcher = patcher;
            public sealed override string Name => patcher.Name;
            public override void Execute() {
                Info("Patching...");
                patcher.Patch();
            }
            public override string Print() => $"[Patch:{patcher.Name}]";
            public override string ToString() => Print();
        }
        class LinkedPatchChain(ILogger logger, PatchChain first, PatchChain next) : PatchChain(logger)
        {
            private readonly PatchChain current = first;
            private readonly PatchChain next = next;
            public sealed override string Name => current.Name;
            public override void Execute() {
                Info("Patching...");
                current.Execute();
                next.Execute();
            }
            public override string Print() => $"{current.Print()} -> {next.Print()}";
            public override string ToString() => Print();
        }
    }
    public class PatchingChain<TArgument>(ILogger logger, ArgumentProvider<TArgument> argument, PatchPipelineBuilder? previous = null) : PatchPipelineBuilder(logger)
        where TArgument : Argument
    {
        protected readonly ArgumentProvider<TArgument> argument = argument;
        private readonly PatchPipelineBuilder? previous = previous;
        private readonly ILogger logger = logger;
        public override string Name { get; } = $"{nameof(PatchChain)}|Args:{typeof(TArgument).Name}";
        /// <summary>
        /// Expose the no-argument execution logic to the outside user, and only handle the argument generation related logic internally
        /// <para>Currently, this instance of <see cref="PatchingChain{TArgument}"/> is only a wrapper of the no-argument logic <see cref="previous"/>. </para>
        /// </summary>
        public override void Execute() {
            previous?.Execute();
        }
        /// <para><see cref="arguments"/> is shared between the tail and ensures that the argument is only constructed once the logic that needs the argument is executed. </para>
        /// <para>This avoids the construction time of the argument that may have side effects on the outside.</para>
        /// <para>Currently, this instance of <see cref="PatchingChain{TArgument}"/> is only a wrapper of the no-argument logic <see cref="previous"/>, </para>
        /// <para>so there is no need to do anything to <see cref="arguments"/>.</para>
        /// </summary>
        /// <param name="arguments"></param>
        protected virtual void Execute(ref TArgument? arguments) {
            previous?.Execute();
        }
        public override string Print() {
            if (previous is not null) {
                return previous.Print();
            }
            return "[Empty]";
        }
        public override string ToString() => Print();
        /// <summary>
        /// Returns a new fluent that has abandoned the reference to the arguments <see cref="TArgument"/>
        /// </summary>
        /// <returns></returns>
        public PatchChain Finalize(Action<TArgument>? callback = null) {
            //if (callback is null) {
            //    return new PatchChain(logger, this);
            //}
            return new FinalizeCallbackInvoker(logger, this, callback, argument);
        }

        public PatchingChain<TArgument> Then(Patcher<TArgument> next) {
            Info($"Adding patcher: {next.Name}");
            return new LinkedPatchChain(logger, this, new SinglePatchProcessor(logger, next, argument), argument);
        }
        class FinalizeCallbackInvoker(ILogger logger, PatchingChain<TArgument> insertAfterExec, Action<TArgument>? callback, ArgumentProvider<TArgument> argument) : PatchChain(logger)
        {
            private readonly ArgumentProvider<TArgument> argument = argument;
            private readonly PatchingChain<TArgument> insertAfterExec = insertAfterExec;
            public sealed override string Name => $"[CALLBACK|Args:{typeof(TArgument).Name}]";
            public sealed override void Execute() {
                TArgument? arg = default;
                insertAfterExec.Execute(ref arg);
                arg ??= argument.Generate();
                callback?.Invoke(arg);
                Info("Argument Finalized");
            }
            public override string Print() {
                return $"{insertAfterExec.Print()} -> {Name}";
            }
            public sealed override string ToString() => Print();
        }
        class SinglePatchProcessor(ILogger logger, Patcher<TArgument> patcher, ArgumentProvider<TArgument> argument) : PatchingChain<TArgument>(logger, argument)
        {
            private readonly Patcher<TArgument> patcher = patcher;
            public sealed override string Name => patcher.Name;
            public sealed override void Execute() {
                var arg = argument.Generate();
                Info("Patching...");
                patcher.Patch(arg);
            }
            protected override void Execute(ref TArgument? arg) {
                arg ??= argument.Generate();
                Info("Patching...");
                patcher.Patch(arg);
            }
            public override string Print() {
                return $"[Patch:{patcher.Name}]";
            }
            public sealed override string ToString() => Print();
        }
        class LinkedPatchChain(ILogger logger, PatchingChain<TArgument> first, PatchingChain<TArgument> next, ArgumentProvider<TArgument> argument) : PatchingChain<TArgument>(logger, argument)
        {
            private readonly PatchingChain<TArgument> current = first;
            private readonly PatchingChain<TArgument> next = next;
            public sealed override string Name => current.Name;
            public sealed override void Execute() {
                TArgument? arg = default;
                Info("Patching...");
                current.Execute(ref arg);
                next.Execute(ref arg);
            }
            protected sealed override void Execute(ref TArgument? arg) {
                Info("Patching...");
                current.Execute(ref arg);
                next.Execute(ref arg);
            }
            public override string Print() {
                return $"{current.Print()} -> {next.Print()}";
            }
            public sealed override string ToString() => Print();
        }
    }
}
