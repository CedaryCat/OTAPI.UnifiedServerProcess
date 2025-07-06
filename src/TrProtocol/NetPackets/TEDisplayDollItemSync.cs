using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TEDisplayDollItemSync : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.TEDisplayDollItemSync;
    public byte PlayerSlot { get; set; }
    public int TileEntityID;
    public byte ItemSlot;
    public ushort ItemID;
    public ushort Stack;
    public byte Prefix;
}