using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct QuickStackChests : INetPacket, IChestSlot {
    public readonly MessageID Type => MessageID.QuickStackChests;
    public short ChestSlot { get; set; }
}