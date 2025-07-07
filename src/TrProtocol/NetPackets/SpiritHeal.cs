using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SpiritHeal : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.SpiritHeal;
    public byte OtherPlayerSlot { get; set; }
    public short Amount;
}