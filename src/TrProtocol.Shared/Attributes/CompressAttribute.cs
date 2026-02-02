using System.IO.Compression;

namespace TrProtocol.Attributes;

/// <summary>
/// Enables compression for the generated <c>WriteContent</c>/<c>ReadContent</c> pipeline of a serializable type.
/// </summary>
/// <remarks>
/// <para>
/// The source generator only allows this attribute on types implementing <c>ILengthAware</c>. When present, generated
/// code will serialize into a temporary buffer and call <c>CommonCode.WriteCompressedData</c>; deserialization will call
/// <c>CommonCode.ReadDecompressedData</c> into a rented buffer.
/// </para>
/// <para>
/// <paramref name="bufferSize"/> is used for renting arrays from <c>ArrayPool&lt;byte&gt;</c> in generated code and must be
/// large enough for the (de)compressed payload for your protocol.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CompressAttribute : Attribute
{
    public readonly CompressionLevel Level;
    public readonly int BufferSize;
    public CompressAttribute(CompressionLevel level, int bufferSize) {
        Level = level;
        BufferSize = bufferSize;
    }
}
