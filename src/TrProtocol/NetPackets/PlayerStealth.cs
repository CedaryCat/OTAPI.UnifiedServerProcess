using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerStealth : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.PlayerStealth;
    public byte PlayerSlot { get; set; }
    public float Stealth;
}