using Mono.Cecil;

namespace OTAPI.UnifiedServerProcess
{
    public static class Constants
    {
        public const string RootContextParamName = "root";
        public const string RootContextLocalName = "root";
        public const string RootContextFieldName = "root";

        public const string ContextSuffix = "SystemContext";
        public const string RootContextName = "RootContext";
        public const string RootContextFullName = "UnifiedServerProcess.RootContext";
        public const string GlobalInitializerTypeName = "UnifiedServerProcess.GlobalInitializer";
        public const string InitializerAttributeTypeName = "UnifiedServerProcess.InitializerExtractFromAttribute";
        public const string GlobalInitializerEntryPointName = "InitializeEntryPoint";

        public static class Modifiers
        {

            public const TypeAttributes ContextType = TypeAttributes.Public | TypeAttributes.Class;
            public const TypeAttributes ContextNestedType = TypeAttributes.NestedPublic | TypeAttributes.Class;

            public const FieldAttributes RootContextField = FieldAttributes.Public | FieldAttributes.InitOnly;

            public const FieldAttributes ContextField = FieldAttributes.Public;
            public const MethodAttributes ContextConstructor = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            public const MethodAttributes GlobalInitialize = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static;
        }
        public static class Patching
        {
            public const string ConvertedFieldInSingletonSuffix = "_ReusedField";
        }
    }
}
