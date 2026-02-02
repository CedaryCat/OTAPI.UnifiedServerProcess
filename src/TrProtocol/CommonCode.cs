using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace TrProtocol;

public static unsafe class CommonCode
{
    static delegate*<int, string> FastAllocateString;
    static CommonCode()
    {
        FastAllocateString = (delegate*<int, string>)typeof(string).GetRuntimeMethods().First(m => m.Name == "FastAllocateString").MethodHandle.GetFunctionPointer();
    }
    #region String
    public static void WriteString(ref void* current, string value)
    {
        Write7BitEncodedInt(ref current, Encoding.UTF8.GetByteCount(value));
        current = Unsafe.Add<byte>(current, Encoding.UTF8.GetBytes(value, new Span<byte>(current, short.MaxValue)));
    }
    public static unsafe void Write7BitEncodedInt(ref void* destination, int value)
    {
        byte* ptr = (byte*)destination;
        uint num = (uint)value;

        while (num >= 0x80)
        {
            *ptr++ = (byte)(num | 0x80);
            num >>= 7;
        }
        *ptr++ = (byte)num;

        destination = ptr;
    }
    public static unsafe string ReadString(ref void* ptr)
    {
        int byteCount = Read7BitEncodedInt(ref ptr);
        var span = new ReadOnlySpan<byte>(ptr, byteCount);
        string result = Encoding.UTF8.GetString(span);
        ptr = Unsafe.Add<byte>(ptr, byteCount);
        return result;
    }

    public static unsafe int Read7BitEncodedInt(ref void* source)
    {
        byte* ptr = (byte*)source;
        int result = 0;
        int bits = 0;
        byte b;

        do
        {
            b = *ptr++;
            result |= (b & 0x7F) << bits;
            bits += 7;
        } while ((b & 0x80) != 0);

        source = ptr;
        return result;
    }
    #endregion

    #region Compression
    public static unsafe void ReadDecompressedData(void* source, ref void* destination, int compressedDataLength)
    {
        using var st = new UnmanagedMemoryStream((byte*)source, compressedDataLength, compressedDataLength, FileAccess.Read);
        using (var dst = new DeflateStream(st, CompressionMode.Decompress, true))
        {
            int readed;
            do
            {
                readed = dst.Read(new Span<byte>(destination, 1024 * 32));
                destination = Unsafe.Add<byte>(destination, readed);
            }
            while (readed > 0);
        }
    }
    public static unsafe void WriteCompressedData(void* source, ref void* destination, int rawDataLength, CompressionLevel level)
    {
        using var st = new UnmanagedMemoryStream((byte*)destination, 1024 * 64, 1024 * 64, FileAccess.Write);
        using (var dst = new DeflateStream(st, level, true))
        {
            dst.Write(new Span<byte>(source, rawDataLength));
        }
        destination = st.PositionPointer;
    }
    #endregion
}
