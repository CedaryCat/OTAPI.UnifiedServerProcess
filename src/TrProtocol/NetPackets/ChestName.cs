using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ChestName : INetPacket, IChestSlot
{
    public readonly MessageID Type => MessageID.ChestName;
    public short ChestSlot { get; set; }
    public Point16 Position;
    public string Name;
}