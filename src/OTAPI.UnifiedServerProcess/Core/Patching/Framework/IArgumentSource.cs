namespace OTAPI.UnifiedServerProcess.Core.Patching.Framework
{
    public interface IArgumentSource<TSelf, TResult>
        where TResult : Argument
        where TSelf : Argument, IArgumentSource<TSelf, TResult>
    {
        public TResult Build();
    }
}
