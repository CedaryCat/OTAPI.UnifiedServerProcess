using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ChestUpdates : INetPacket, IChestSlot
{
    public readonly MessageID Type => MessageID.ChestUpdates;
    public byte Operation;
    public Point16 Position;
    public short Style;
    public short ChestSlot { get; set; }
}