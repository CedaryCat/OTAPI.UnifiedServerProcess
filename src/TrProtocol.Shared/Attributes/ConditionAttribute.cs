namespace TrProtocol.Attributes;

/// <summary>
/// Conditionally serializes a member based on the value of another member.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. When present, generated code wraps the member's
/// serialization/deserialization in an <c>if (...)</c> block.
/// </para>
/// <para>
/// Forms:
/// </para>
/// <list type="bullet">
/// <item><description><c>[Condition(nameof(SomeBoolMember), pred: true)]</c> &rarr; <c>if (SomeBoolMember)</c></description></item>
/// <item><description><c>[Condition(nameof(SomeBitsByteMember), index: 0, pred: true)]</c> &rarr; <c>if (SomeBitsByteMember[0])</c></description></item>
/// </list>
/// <para>
/// Notes (per generator validation):
/// </para>
/// <list type="bullet">
/// <item><description>If the condition makes a reference-type member optional, the member must be declared nullable; otherwise the generator reports an error (e.g. <c>SCG19</c>).</description></item>
/// <item><description>The referenced member must exist and be compatible with the chosen form (boolean vs bit-indexed).</description></item>
/// </list>
/// <para>
/// Multiple condition attributes within the same attribute list are combined with logical AND; conditions across separate
/// attribute lists are combined with logical OR.
/// </para>
/// </remarks>

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public class ConditionAttribute : Attribute
{
    public readonly string field;
    public readonly int bit;
    public readonly bool pred;
    /// <summary>
    /// Creates a bit-indexed condition (e.g. <c>Flags[0]</c>).
    /// </summary>
    /// <param name="fieldOrProperty">The name of the member used as the condition source (typically <c>nameof(...)</c>).</param>
    /// <param name="index">The bit index (0-based).</param>
    /// <param name="pred">Expected value; when false the condition is negated.</param>
    public ConditionAttribute(string fieldOrProperty, byte index, bool pred = true) : this(fieldOrProperty, (int)index, pred) {

    }
    private ConditionAttribute(string fieldOrProperty, int index, bool pred = true) {
        this.bit = index;
        this.field = fieldOrProperty;
        this.pred = pred;
    }
    /// <summary>
    /// Creates a boolean condition (e.g. <c>HasFlag</c>).
    /// </summary>
    /// <param name="fieldOrProperty">The name of the member used as the condition source (typically <c>nameof(...)</c>).</param>
    /// <param name="pred">Expected value; when false the condition is negated.</param>
    public ConditionAttribute(string fieldOrProperty, bool pred = true) : this(fieldOrProperty, -1, pred) {
    }
}
