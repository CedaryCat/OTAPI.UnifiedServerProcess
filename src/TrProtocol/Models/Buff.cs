using TrProtocol.Interfaces;
using System.Runtime.InteropServices;

namespace TrProtocol.Models;

[StructLayout(LayoutKind.Sequential)]
public partial struct Buff : IPackedSerializable {
    public ushort BuffType;
    public short BuffTime;
}
