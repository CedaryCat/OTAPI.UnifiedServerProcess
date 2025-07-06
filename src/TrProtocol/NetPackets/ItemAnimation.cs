using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ItemAnimation : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.ItemAnimation;
    public byte PlayerSlot { get; set; }
    public float Rotation;
    public short Animation;
}