using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework {
    public struct RGBColor : IPackedSerializable {
        public byte R;
        public byte G;
        public byte B;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct Color : IEquatable<Color>, IEquatable<RGBColor>, ISerializableView<RGBColor> {
        public RGBColor View {
            readonly get => rgb;
            set => rgb = value;
        }
        [FieldOffset(0)] public byte r;
        [FieldOffset(1)] private byte g;
        [FieldOffset(2)] private byte b;
        [FieldOffset(3)] private byte a;
        [FieldOffset(0)] private RGBColor rgb;
        [FieldOffset(0)] public uint packedValue;

        public byte R {
            readonly get => r;
            set => r = value;
        }

        public byte G {
            readonly get => g;
            set => g = value;
        }

        public byte B {
            readonly get => b;
            set => b = value;
        }

        public byte A {
            readonly get => a;
            set => a = value;
        }

        public uint PackedValue {
            readonly get => packedValue;
            set => packedValue = value;
        }

        public readonly override string ToString() => string.Format(CultureInfo.CurrentCulture, "{{R:{0} G:{1} B:{2} A:{3}}}", r, g, b, a);
        public readonly override int GetHashCode() => packedValue.GetHashCode();
        public readonly override bool Equals(object? obj) =>  obj is Color other && Equals(other);
        public readonly bool Equals(Color other) => packedValue.Equals(other.packedValue);
        public bool Equals(RGBColor other) => R == other.R && G == other.G && B == other.B;
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
    }
}
