using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncPlayerChestIndex : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncPlayerChestIndex;
    public byte PlayerSlot { get; set; }
    public short ChestIndex;
}