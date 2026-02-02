namespace TrProtocol.Attributes;

/// <summary>
/// Declares the length for each rank of an array member so the source generator can emit fixed-size
/// serialization/deserialization loops.
/// </summary>
/// <remarks>
/// <para>
/// Required by <c>TrProtocol.SerializerGenerator</c> for most array members; missing or mismatched ranks will produce
/// generator diagnostics (e.g. <c>SCG23</c>/<c>SCG24</c>/<c>SCG25</c>).
/// </para>
/// <para>
/// Each argument is interpreted syntactically by the generator. Supported forms include:
/// </para>
/// <list type="bullet">
/// <item><description>A numeric literal (must fit in <see cref="ushort"/>).</description></item>
/// <item><description>A string literal naming a member (e.g. <c>"Count"</c>), or <c>nameof(Count)</c>.</description></item>
/// </list>
/// <para>
/// For one-dimensional arrays this should be a single argument; for multi-rank arrays the argument count must match
/// the declared array rank.
/// </para>
/// </remarks>

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ArraySizeAttribute : Attribute
{
    public readonly object[] LengthOfEachRank;
    /// <summary>
    /// Creates an <see cref="ArraySizeAttribute"/>.
    /// </summary>
    /// <param name="each">One size expression per array rank.</param>
    public ArraySizeAttribute(params object[] each) {
        LengthOfEachRank = each;
    }
}
