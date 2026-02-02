namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes a member when <c>fieldOrProperty == pred</c>.
/// </summary>
/// <remarks>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> as a comparison condition.
/// The right-hand value is emitted syntactically, and is typically a numeric literal, <c>sizeof(...)</c>, or an enum
/// member access.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConditionEqualAttribute : Attribute
{
    public readonly string fieldOrProperty;
    public readonly object pred;
    public ConditionEqualAttribute(string fieldOrProperty, object pred) {
        this.fieldOrProperty = fieldOrProperty;
        this.pred = pred;
    }
}
