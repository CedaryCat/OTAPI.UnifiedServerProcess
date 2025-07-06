using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncLoadout : INetPacket, IPlayerSlot, ILoadOutSlot {
    public readonly MessageID Type => MessageID.SyncLoadout;
    public byte PlayerSlot { get; set; }
    public byte LoadOutSlot { get; set; }
    public ushort AccessoryVisibility;
}