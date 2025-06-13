namespace OTAPI.UnifiedServerProcess.Loggers.Implements
{
    public class DefaultLogger(int minLevel) : CompositeLogger(
        new ConsoleLogger(minLevel)
        /*,new FileLogger()*/)
    {
    }
}
