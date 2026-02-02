namespace Microsoft.Xna.Framework.Graphics.PackedVector
{

    public struct HalfVector2 : IEquatable<HalfVector2>
    {
        private uint packedValue;

        public uint PackedValue {
            readonly get {
                return packedValue;
            }
            set {
                packedValue = value;
            }
        }

        public HalfVector2(float x, float y) {
            packedValue = PackHelper(x, y);
        }

        public HalfVector2(Vector2 vector) {
            packedValue = PackHelper(vector.X, vector.Y);
        }

        private static uint PackHelper(float vectorX, float vectorY) {
            uint num = HalfUtils.Pack(vectorX);
            uint num2 = (uint)(HalfUtils.Pack(vectorY) << 16);
            return num | num2;
        }

        public readonly Vector2 ToVector2() {
            Vector2 result = default(Vector2);
            result.X = HalfUtils.Unpack((ushort)packedValue);
            result.Y = HalfUtils.Unpack((ushort)(packedValue >> 16));
            return result;
        }

        public readonly override string ToString() {
            return ToVector2().ToString();
        }

        public readonly override int GetHashCode() {
            return packedValue.GetHashCode();
        }

        public readonly override bool Equals(object? obj) {
            if (obj is HalfVector2 v) {
                return Equals(v);
            }

            return false;
        }

        public readonly bool Equals(HalfVector2 other) {
            return packedValue.Equals(other.packedValue);
        }

        public static bool operator ==(HalfVector2 a, HalfVector2 b) {
            return a.Equals(b);
        }

        public static bool operator !=(HalfVector2 a, HalfVector2 b) {
            return !a.Equals(b);
        }
    }
}
