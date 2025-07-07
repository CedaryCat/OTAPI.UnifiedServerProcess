using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerZone : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerZone;
    public byte PlayerSlot { get; set; }
    [ArraySize(5)]
    public byte[] Zone;
}