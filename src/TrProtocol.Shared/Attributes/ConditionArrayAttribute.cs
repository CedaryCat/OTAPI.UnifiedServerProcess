namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes array elements based on another indexable member.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> during array-element expansion. The generator emits element-level
/// guards of the form <c>if (ConditionSource[i + indexStart])</c> (or negated when <paramref name="pred"/> is false).
/// </para>
/// <para>
/// This is only supported for one-dimensional arrays; applying it to multi-dimensional arrays will result in a generator
/// error (e.g. <c>SCG20</c>).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public class ConditionArrayAttribute : Attribute
{
    public readonly string field;
    public readonly byte indexStart;
    public readonly bool pred;
    public ConditionArrayAttribute(string fieldOrProperty, byte indexStart, bool pred = true) {
        this.indexStart = indexStart;
        this.field = fieldOrProperty;
        this.pred = pred;
    }
}
