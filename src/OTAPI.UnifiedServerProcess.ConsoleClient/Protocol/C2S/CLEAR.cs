using System.Runtime.InteropServices;

namespace OTAPI.UnifiedServerProcess.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CLEAR : IEmptyPacket<CLEAR>
    {
        public const int id = 0x01;
        public static int ID => id;
        public override readonly string ToString() {
            return nameof(CLEAR);
        }
    }
}
