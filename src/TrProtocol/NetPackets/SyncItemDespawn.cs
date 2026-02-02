using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncItemDespawn : INetPacket, IItemSlot
{
    public readonly MessageID Type => MessageID.SyncItemDespawn;
    public short ItemSlot { get; set; }
}
