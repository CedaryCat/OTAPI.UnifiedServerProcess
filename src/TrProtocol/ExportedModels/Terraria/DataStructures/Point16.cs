using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;
using TrProtocol.Interfaces;

namespace Terraria.DataStructures;

[StructLayout(LayoutKind.Explicit)]
public struct Point16 : IPackedSerializable, IEquatable<Point16>
{
    [FieldOffset(0)] public short X;
    [FieldOffset(2)] public short Y;
    [FieldOffset(0)] uint packedValue;
    public Point16(Point point) {
        X = (short)point.X;
        Y = (short)point.Y;
    }

    public Point16(int X, int Y) {
        this.X = (short)X;
        this.Y = (short)Y;
    }

    public Point16(short X, short Y) {
        this.X = X;
        this.Y = Y;
    }
    public static bool operator ==(Point16 first, Point16 second) => first.Equals(second);
    public static bool operator !=(Point16 first, Point16 second) => !first.Equals(second);
    public override readonly bool Equals(object? obj) => obj is Point16 point && Equals(point);
    public readonly bool Equals(Point16 other) => packedValue == other.packedValue;
    public override readonly int GetHashCode() => packedValue.GetHashCode();
    public override readonly string ToString() {
        return $"{{{X}, {Y}}}";
    }
}
