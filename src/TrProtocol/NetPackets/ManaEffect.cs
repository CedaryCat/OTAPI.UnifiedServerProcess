using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ManaEffect : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.ManaEffect;
    public byte PlayerSlot { get; set; }
    public short Amount;
}