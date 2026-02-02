using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SpectatePlayer : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SpectatePlayer;
    public byte PlayerSlot { get; set; }
    public short SpectatingPlayerIndex;
}
