namespace TrProtocol.Attributes;

/// <summary>
/// Marks a member as an "external member" that is supplied from outside the wire format.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> when generating constructors and polymorphic <c>Read</c> helpers.
/// Members decorated with this attribute are collected and exposed as additional parameters so callers can provide
/// values that are not serialized as part of the packet payload.
/// </para>
/// <para>
/// This is also used together with <see cref="ExternalMemberValueAttribute"/> to allow nested serialization to set
/// default values on inner members that are marked as external.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ExternalMemberAttribute : Attribute
{
}
