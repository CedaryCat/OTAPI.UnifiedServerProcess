namespace TrProtocol.Attributes;

/// <summary>
/// Serializes a one-dimensional array by reading elements until a terminator is encountered,
/// or until a maximum element count is reached.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> for one-dimensional arrays.
/// Deserialization reads elements sequentially and stops when either:
/// <list type="bullet">
/// <item><description><paramref name="maxCount"/> elements have been read, or</description></item>
/// <item><description>a terminator value is encountered while reading an element.</description></item>
/// </list>
/// </para>
/// <para>
/// The terminator is compared against a primitive value type <em>termination key</em>.
/// If the element type <c>T</c> is a primitive value type, the termination key is the element itself.
/// Otherwise, <c>T</c> must be a struct whose first public serializable member (field or property) is a primitive
/// value type (or an enum backed by a primitive); in that case the termination key is the value of that first member.
/// </para>
/// <para>
/// <paramref name="terminator"/> is an optional terminator expression; if omitted the generator uses
/// <c>default</c> of the termination key type.
/// The expression must be compatible with the inferred termination key type.
/// </para>
/// <para>
/// <paramref name="maxCount"/> is emitted syntactically by the generator (e.g. a literal, <c>nameof(...)</c>,
/// or another expression) and serves as an upper bound to prevent unbounded reads.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TerminatedArrayAttribute(object maxCount, object? terminator = null) : Attribute
{
    public readonly object MaxCount = maxCount;
    public readonly object? Terminator = terminator;
}
