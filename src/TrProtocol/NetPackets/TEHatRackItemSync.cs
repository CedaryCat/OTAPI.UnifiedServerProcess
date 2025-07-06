using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TEHatRackItemSync : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.TEHatRackItemSync;
    public byte PlayerSlot { get; set; }
    public int TileEntityID;
    public byte ItemSlot;
    public ushort ItemID;
    public ushort Stack;
    public byte Prefix;
}