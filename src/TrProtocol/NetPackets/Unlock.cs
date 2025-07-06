using TrProtocol.Models.Interfaces;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct Unlock : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.Unlock;
    public byte PlayerSlot { get; set; }
    public Point16 Position;
}