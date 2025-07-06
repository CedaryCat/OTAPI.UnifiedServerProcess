using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ResetItemOwner : INetPacket, IItemSlot {
    public readonly MessageID Type => MessageID.ResetItemOwner;
    public short ItemSlot { get; set; }
}