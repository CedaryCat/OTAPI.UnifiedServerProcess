using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerPvP : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.PlayerPvP;
    public byte PlayerSlot { get; set; }
    public bool Pvp;
}