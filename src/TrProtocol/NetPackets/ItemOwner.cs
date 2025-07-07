using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ItemOwner : INetPacket, IItemSlot, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.ItemOwner;
    public short ItemSlot { get; set; }
    public byte OtherPlayerSlot { get; set; }
}
