using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerActive : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.PlayerActive;
    public byte PlayerSlot { get; set; }
    public bool Active;
}
