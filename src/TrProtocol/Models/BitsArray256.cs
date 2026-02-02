using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

public struct BitsArray256 : IPackedSerializable
{
    [InlineArray(32)]
    struct Thunk
    {
        public byte e0;
    }

    Thunk data;

    /// <summary>
    /// Gets/sets a single bit by index in range [0, 255].
    /// Bit numbering: bit 0 is the least-significant bit (LSB) of byte 0.
    /// </summary>
    public bool this[int bitIndex] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get {
            // One unsigned range check handles negative and >= 256 in one compare.
            if ((uint)bitIndex >= 256u) ThrowHelper.ThrowBitIndexOutOfRange();

            int byteIndex = bitIndex >> 3;              // bitIndex / 8
            int bitOffset = bitIndex & 7;               // bitIndex % 8
            byte mask = (byte)(1u << bitOffset);

            // In readonly context, use Unsafe.AsRef(in ...) to get a ref without copying.
            ref byte b = ref Unsafe.Add(ref Unsafe.AsRef(in data.e0), byteIndex);
            return (b & mask) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            if ((uint)bitIndex >= 256u) ThrowHelper.ThrowBitIndexOutOfRange();

            int byteIndex = bitIndex >> 3;
            int bitOffset = bitIndex & 7;
            byte mask = (byte)(1u << bitOffset);

            ref byte b = ref Unsafe.Add(ref data.e0, byteIndex);
            b = value ? (byte)(b | mask) : (byte)(b & (byte)~mask);
        }
    }

    /// <summary>
    /// Exposes the underlying 32 bytes as a Span for fast bulk/vectorized operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref data.e0, 32);

    /// <summary>
    /// Read-only view of the underlying 32 bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> AsReadOnlySpan() =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data.e0), 32);

    private static class ThrowHelper
    {
        // Keep throw paths out of the hot path (NoInlining) to help JIT generate tighter code.
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowBitIndexOutOfRange() =>
            throw new IndexOutOfRangeException("bitIndex must be in range [0, 255].");

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowByteIndexOutOfRange() =>
            throw new IndexOutOfRangeException("byteIndex must be in range [0, 31].");
    }
}
