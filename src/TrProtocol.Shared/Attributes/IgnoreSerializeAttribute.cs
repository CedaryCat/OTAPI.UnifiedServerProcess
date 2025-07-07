namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IgnoreSerializeAttribute : Attribute { }
}
