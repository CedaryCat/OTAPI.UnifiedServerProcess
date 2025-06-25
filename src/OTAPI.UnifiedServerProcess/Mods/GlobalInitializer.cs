#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS0436 // Type conflicts with imported type
using ModFramework;
using System;

[Modification(ModType.PreRead, "Add Global Initializer", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void MergeRootContext(ModFwModder modder) {
    Console.WriteLine(modder.Module.GetType("UnifiedServerProcess.GlobalInitializer").FullName);
}

namespace UnifiedServerProcess
{
    public static class GlobalInitializer
    {
        public static void Initialize() { }
    }
    public class InitializerExtractFromAttribute(Type type, string method) : Attribute
    {
        public readonly Type originalType = type;
        public readonly string MethodName = method;
    }
}
