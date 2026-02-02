namespace TrProtocol.Attributes;

/// <summary>
/// Serializes an integer using 7-bit encoding in source-generated code.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c>. Supported targets include:
/// </para>
/// <list type="bullet">
/// <item><description><c>int</c> members.</description></item>
/// <item><description>Enums whose underlying type is <c>int</c>.</description></item>
/// <item><description>One-dimensional <c>int[]</c> or <c>int</c>-backed enum arrays (requires <see cref="ArraySizeAttribute"/>).</description></item>
/// </list>
/// <para>
/// The generator emits calls to <c>CommonCode.Write7BitEncodedInt</c> / <c>CommonCode.Read7BitEncodedInt</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class Int7BitEncodedAttribute : Attribute
{
}
