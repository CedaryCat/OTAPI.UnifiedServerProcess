using System.Runtime.InteropServices;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

[StructLayout(LayoutKind.Sequential)]
public partial struct Buff : IPackedSerializable
{
    public ushort BuffType;
    public short BuffTime;
}
