namespace TrProtocol.NetPackets;

public partial struct LiquidUpdate : INetPacket
{
    public readonly MessageID Type => MessageID.LiquidUpdate;
    public short TileX;
    public short TileY;
    public byte Liquid;
    public byte LiquidType;
}
