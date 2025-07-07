using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct HealEffect : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.HealEffect;
    public byte PlayerSlot { get; set; }
    public short Amount;
}