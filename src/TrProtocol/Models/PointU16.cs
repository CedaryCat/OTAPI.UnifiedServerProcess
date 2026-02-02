using System.Runtime.InteropServices;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

[StructLayout(LayoutKind.Explicit)]
public struct PointU16 : IPackedSerializable, IEquatable<PointU16>
{
    [FieldOffset(0)]
    public ushort X;
    [FieldOffset(2)]
    public ushort Y;
    [FieldOffset(0)]
    private uint packedValue;

    public static bool operator ==(PointU16 a, PointU16 b) => a.Equals(b);
    public static bool operator !=(PointU16 a, PointU16 b) => !a.Equals(b);
    public override readonly bool Equals(object? obj) => obj is PointU16 other && Equals(other);
    public override readonly int GetHashCode() => packedValue.GetHashCode();
    public readonly bool Equals(PointU16 other) => packedValue == other.packedValue;
}
