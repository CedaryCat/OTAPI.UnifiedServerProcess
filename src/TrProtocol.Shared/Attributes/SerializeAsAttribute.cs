namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public class SerializeAsAttribute : Attribute
    {
        public Type TargetType;
        public SerializeAsAttribute(Type numberType) {
            TargetType = numberType;
        }
    }
}
