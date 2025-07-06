using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayNote : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.PlayNote;
    public byte PlayerSlot { get; set; }
    public float Range;
}