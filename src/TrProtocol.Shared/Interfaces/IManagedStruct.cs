namespace TrProtocol.Interfaces;

/// <summary>
/// Marker interface indicating that the type is a managed packet type
/// and therefore does <b>not</b> satisfy the <c>unmanaged</c> constraint.
/// </summary>
/// <remarks>
/// <para>
/// This interface is used to explicitly mark types that require garbage-collected (managed)
/// memory and cannot be used in contexts constrained to <c>unmanaged</c> types.
/// </para>
/// <para>
/// It is particularly useful in high-performance or source-generated serialization scenarios
/// where type paths must distinguish between <c>unmanaged</c> types and their managed counterparts.
/// </para>
/// <para>
/// Unlike typical marker interfaces, <see cref="IManagedPacket"/> does not have an explicit opposite like
/// <c>IUnmanagedPacket</c>; instead, its semantic opposite is expressed via the C# <c>unmanaged</c> generic constraint.
/// </para>
/// </remarks>
public interface IManagedPacket { }
