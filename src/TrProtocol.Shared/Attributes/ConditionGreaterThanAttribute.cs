namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes a member when <c>fieldOrProperty &gt; check</c>.
/// </summary>
/// <remarks>Interpreted by <c>TrProtocol.SerializerGenerator</c> as a comparison condition.</remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionGreaterThanAttribute : Attribute
{
    public readonly string fieldOrProperty;
    public readonly int check;
    public ConditionGreaterThanAttribute(string fieldOrProperty, int check) {
        this.fieldOrProperty = fieldOrProperty;
        this.check = check;
    }
}

/// <summary>
/// Conditionally serializes a member when <c>fieldOrProperty &gt;= check</c>.
/// </summary>
/// <remarks>Interpreted by <c>TrProtocol.SerializerGenerator</c> as a comparison condition.</remarks>
public class ConditionGreaterThanEqualAttribute : Attribute
{
    public readonly string fieldOrProperty;
    public readonly int check;
    public ConditionGreaterThanEqualAttribute(string fieldOrProperty, int check) {
        this.fieldOrProperty = fieldOrProperty;
        this.check = check;
    }
}
