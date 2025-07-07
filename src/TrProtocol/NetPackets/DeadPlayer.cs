using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct DeadPlayer : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.DeadPlayer;
    public byte OtherPlayerSlot { get; set; }
}