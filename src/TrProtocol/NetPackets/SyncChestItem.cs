using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncChestItem : INetPacket, IChestSlot {
    public readonly MessageID Type => MessageID.SyncChestItem;
    public short ChestSlot { get; set; }
    public byte ChestItemSlot;
    public short Stack;
    public byte Prefix;
    public short ItemType;
}