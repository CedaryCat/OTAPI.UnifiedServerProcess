using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct RequestTileEntityInteraction : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.RequestTileEntityInteraction;
    public int TileEntityID;
    public byte PlayerSlot { get; set; }
}