namespace TrProtocol.Attributes;

/// <summary>
/// Marks a member as only applicable for the client-to-server direction.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. The declaring type must implement <c>ISideSpecific</c> so the
/// generator can use <c>IsServerSide</c> in emitted condition checks.
/// </para>
/// <para>
/// For generated write code, this becomes <c>if (!IsServerSide)</c>. For generated read code, the condition is inverted
/// so that only the correct side consumes the member.
/// </para>
/// <para>
/// This attribute is mutually exclusive with <see cref="S2COnlyAttribute"/>; applying both produces a generator error
/// (e.g. <c>SCG03</c>).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class C2SOnlyAttribute : Attribute
{

}
