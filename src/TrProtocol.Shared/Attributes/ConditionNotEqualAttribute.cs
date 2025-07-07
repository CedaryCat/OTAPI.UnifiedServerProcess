namespace TrProtocol.Attributes
{

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConditionNotEqualAttribute : Attribute
    {
        public readonly string fieldOrProperty;
        public readonly object pred;
        public ConditionNotEqualAttribute(string fieldOrProperty, object pred) {
            this.fieldOrProperty = fieldOrProperty;
            this.pred = pred;
        }
    }
}
