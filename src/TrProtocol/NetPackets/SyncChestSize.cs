using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncChestSize : INetPacket, IChestSlot
{
    public readonly MessageID Type => MessageID.SyncChestSize;
    public short ChestSlot { get; set; }
    public short NewSize;
}
