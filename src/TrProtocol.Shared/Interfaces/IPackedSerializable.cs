namespace TrProtocol.Interfaces;

/// <summary>
/// Marker interface for structs where auto layout is guaranteed to be identical to sequential layout with no padding.
/// 
/// This guarantees that the struct's memory representation is contiguous and field order matches declaration order,
/// enabling direct binary serialization via unsafe memory operations.
/// 
/// WARNING: Only implement this interface if you have verified that:
///   1. The struct has no implicit padding between fields
///   2. Auto and Sequential layouts produce identical memory layouts
///   3. The struct contains only unmanaged types
/// 
/// When in doubt, explicitly apply [StructLayout(LayoutKind.Sequential, Pack = 1)] to the struct.
/// </summary>
public interface IPackedSerializable
{
}
