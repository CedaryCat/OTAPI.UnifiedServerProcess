namespace TrProtocol.Attributes;

/// <summary>
/// Declares which discriminator enum value an interface represents when it inherits a polymorphic base interface.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. When an interface inherits another interface decorated with
/// <see cref="PolymorphicBaseAttribute"/>, it cannot implement the discriminator property; instead it must provide
/// an <see cref="ImplementationClaimAttribute"/> whose argument is a member access of the discriminator enum.
/// </para>
/// <para>
/// The generator validates that the enum type matches the discriminator enum required by the base interface; otherwise
/// it reports a diagnostic (e.g. <c>SCG05</c>).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class ImplementationClaimAttribute : Attribute
{
    public readonly Enum ImplementationIdentity;
    public ImplementationClaimAttribute(object implIdentity) {
        ImplementationIdentity = (Enum)implIdentity;
    }
}
