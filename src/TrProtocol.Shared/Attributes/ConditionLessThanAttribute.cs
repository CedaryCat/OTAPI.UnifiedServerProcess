namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes a member when <c>fieldOrProperty &lt; check</c>.
/// </summary>
/// <remarks>Interpreted by <c>TrProtocol.SerializerGenerator</c> as a comparison condition.</remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionLessThanAttribute : Attribute
{
    public readonly string fieldOrProperty;
    public readonly int check;
    public ConditionLessThanAttribute(string fieldOrProperty, int check) {
        this.fieldOrProperty = fieldOrProperty;
        this.check = check;
    }
}

/// <summary>
/// Conditionally serializes a member when <c>fieldOrProperty &lt;= check</c>.
/// </summary>
/// <remarks>Interpreted by <c>TrProtocol.SerializerGenerator</c> as a comparison condition.</remarks>
public class ConditionLessThanEqualAttribute : Attribute
{
    public readonly string fieldOrProperty;
    public readonly int check;
    public ConditionLessThanEqualAttribute(string fieldOrProperty, int check) {
        this.fieldOrProperty = fieldOrProperty;
        this.check = check;
    }
}
