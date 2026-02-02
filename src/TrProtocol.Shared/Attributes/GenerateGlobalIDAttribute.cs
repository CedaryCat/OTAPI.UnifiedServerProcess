namespace TrProtocol.Attributes;

/// <summary>
/// Requests that the source generator allocate stable, sequential global IDs for a polymorphic hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> for types participating in <see cref="PolymorphicBaseAttribute"/>.
/// When applied to a polymorphic root interface, the generator assigns <c>GlobalID</c> values to each concrete
/// implementation (in discriminator order) and emits:
/// </para>
/// <list type="bullet">
/// <item><description><c>public static int GlobalID =&gt; ...</c> on each concrete type.</description></item>
/// <item><description><c>public const int GlobalIDCount = ...</c> and <c>public static abstract int GlobalID</c> on the root.</description></item>
/// </list>
/// <para>
/// This attribute should only appear on the root; applying it to derived types produces a generator error (e.g. <c>SCG10</c>).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public class GenerateGlobalIDAttribute : Attribute
{
}
