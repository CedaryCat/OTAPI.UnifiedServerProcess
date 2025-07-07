using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SetCountsAsHostForGameplay : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.SetCountsAsHostForGameplay;
    public byte OtherPlayerSlot { get; set; }
    public bool Flag;
}