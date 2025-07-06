using TrProtocol.Interfaces;
using System.Runtime.InteropServices;
using Terraria.DataStructures;

namespace TrProtocol.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LiquidChange : IPackedSerializable {
    public Point16 Position;
    public byte LiquidAmount;
    public LiquidType LiquidType;
}
