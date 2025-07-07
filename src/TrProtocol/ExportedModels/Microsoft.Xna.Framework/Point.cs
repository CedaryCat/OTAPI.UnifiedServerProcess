using System.Runtime.InteropServices;
using TrProtocol.Interfaces;

namespace Microsoft.Xna.Framework
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Point(int x, int y) : IPackedSerializable, IEquatable<Point>
    {
        [FieldOffset(0)]
        public int X = x;
        [FieldOffset(4)]
        public int Y = y;
        [FieldOffset(0)]
        private ulong packedValue;

        public static bool operator ==(Point a, Point b) => a.Equals(b);
        public static bool operator !=(Point a, Point b) => !a.Equals(b);
        public readonly override bool Equals(object? obj) => obj is Point other && Equals(other);
        public readonly override int GetHashCode() => packedValue.GetHashCode();
        public readonly bool Equals(Point other) => packedValue == other.packedValue;
    }
}
