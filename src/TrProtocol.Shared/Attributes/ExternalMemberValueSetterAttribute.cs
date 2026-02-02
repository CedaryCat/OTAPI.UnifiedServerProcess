namespace TrProtocol.Attributes;

/// <summary>
/// Provides a default value for an inner member marked with <see cref="ExternalMemberAttribute"/> on the member's type.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. When this attribute is applied to a member of a serializable
/// type, the generator will emit assignments to the target inner member before writing, and will forward corresponding
/// arguments when constructing/reading the value (depending on the strategy used for the member type).
/// </para>
/// <para>
/// The generator validates that the referenced inner member exists on the target type (or array element type) and that
/// the inner member is also decorated with <see cref="ExternalMemberAttribute"/>; otherwise it reports a diagnostic.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ExternalMemberValueAttribute : Attribute
{
    public readonly string MemberName;
    public readonly object DefaultValue;
    public ExternalMemberValueAttribute(string member, object value) {
        MemberName = member;
        DefaultValue = value;
    }
}
