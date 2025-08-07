using System.Runtime.InteropServices;
using System.Text;

namespace OTAPI.UnifiedServerProcess.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_OUTPUT_ENCODING(Encoding encoding) : IUnmanagedPacket<SET_OUTPUT_ENCODING>
    {
        public const int id = 0x07;
        public static int ID => id;
        int encodingCodePage = encoding.CodePage;
        public Encoding Encoding {
            readonly get {
                return Encoding.GetEncoding(encodingCodePage);
            }
            set {
                encodingCodePage = value.CodePage;
            }
        }
        public override readonly string ToString() {
            return nameof(SET_OUTPUT_ENCODING) + ':' + Encoding;
        }
    }
}
