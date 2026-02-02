namespace TrProtocol.Attributes;

/// <summary>
/// Declares a type as the base of a discriminator-driven polymorphic hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> to generate a static <c>Read{T}</c> method that reads a
/// discriminator value and dispatches to the correct concrete implementation.
/// </para>
/// <para>
/// <paramref name="enumIdentity"/> is the discriminator enum type; <paramref name="identityPropName"/> is the member
/// name (field or property) that provides the discriminator value on concrete implementations.
/// </para>
/// <para>
/// For concrete classes/structs, the generator validates that the discriminator member is implemented as an expression-bodied
/// accessor that returns a constant of the discriminator enum. For derived interfaces, use <see cref="ImplementationClaimAttribute"/>
/// to declare the discriminator value.
/// </para>
/// <para>
/// If the discriminator member is decorated with <see cref="Int7BitEncodedAttribute"/>, the generator will read the
/// discriminator using 7-bit encoding in the generated dispatch method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class PolymorphicBaseAttribute : Attribute
{
    public readonly Type EnumIdentity;
    public readonly string IdentityName;
    public PolymorphicBaseAttribute(Type enumIdentity, string identityPropName) {
        EnumIdentity = enumIdentity;
        IdentityName = identityPropName;
    }
}
