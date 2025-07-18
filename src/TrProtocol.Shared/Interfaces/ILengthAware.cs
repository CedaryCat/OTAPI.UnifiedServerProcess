namespace TrProtocol.Interfaces
{
    /// <summary>
    /// Indicates that the implementing type requires knowledge of its total serialized length
    /// during deserialization due to compression or trailing variable-length data.
    /// </summary>
    /// <remarks>
    /// Source generators will recognize this interface and generate additional logic to:
    /// <list type="bullet">
    /// <item>Handle compressed data that needs decompression context</item>
    /// <item>Process trailing variable-length segments (e.g., ExtraData: byte[])</item>
    /// </list>
    /// </remarks>
    public interface ILengthAware
    {
        /// <summary>
        /// Reads the packet content from the specified memory range
        /// </summary>
        /// <param name="ptr">Current read position (updated during parsing)</param>
        /// <param name="end_ptr">Exclusive end boundary of the available data</param>
        unsafe void ReadContent(ref void* ptr, void* end_ptr);
        unsafe void WriteContent(ref void* ptr);
    }
    /// <summary>
    /// Marker interface indicating that the type is not <see cref="ILengthAware"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is automatically added by source generators to all types that do <b>not</b> implement
    /// <see cref="ILengthAware"/>. Developers do <b>not</b> need to manually implement this interface.
    /// </para>
    /// <para>
    /// Its primary purpose is to enable generic constraints that effectively express
    /// <c>not ILengthAware</c> in high-performance scenarios involving <c>struct</c>-based type specialization.
    /// This allows source generators or AOT-optimized code paths to distinguish between
    /// <see cref="ILengthAware"/> and non-specific types at compile time.
    /// </para>
    /// </remarks>
    public interface INonLengthAware { }
}
