using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct Dodge : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.Dodge;
    public byte PlayerSlot { get; set; }
    public byte DodgeType;
}