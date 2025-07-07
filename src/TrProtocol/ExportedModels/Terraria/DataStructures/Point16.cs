using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;
using TrProtocol.Interfaces;

namespace Terraria.DataStructures
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Point16 : IPackedSerializable, IEquatable<Point16>
    {
        [FieldOffset(0)]
        public short X;
        [FieldOffset(1)]
        public short Y;
        [FieldOffset(0)]
        uint packedValue;

        public readonly static Point16 Zero = new(0, 0);
        public readonly static Point16 NegativeOne = new(-1, -1);

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
        public readonly override bool Equals(object? obj) => obj is Point16 point && Equals(point);
        public readonly bool Equals(Point16 other) => packedValue == other.packedValue;
        public readonly override int GetHashCode() => packedValue.GetHashCode();
        public readonly override string ToString() {
            return $"{{{X}, {Y}}}";
        }
    }
}
