namespace TrProtocol.Attributes;

/// <summary>
/// Serializes an array in a sparse form by emitting only non-default elements.
/// </summary>
/// <remarks>
/// <para>
/// Interpreted by <c>TrProtocol.SerializerGenerator</c> for one-dimensional arrays of numeric element types.
/// Serialization emits pairs of <c>(ushort index, T value)</c> for elements whose value is not <c>default</c>, and then a
/// terminator index.
/// </para>
/// <para>
/// <paramref name="size"/> controls the allocated array length during deserialization, and is emitted syntactically by
/// the generator (e.g. a literal, <c>nameof(...)</c>, or another expression).
/// </para>
/// <para>
/// <paramref name="terminator"/> is an optional terminator index expression; if omitted the generator uses
/// <c>ushort.MaxValue</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SparseArrayAttribute(object size, object? terminator = null) : Attribute
{
    public readonly object Size = size;
    public readonly object? Terminator = terminator;
}
