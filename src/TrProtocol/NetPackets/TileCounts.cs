namespace TrProtocol.NetPackets;

public partial struct TileCounts : INetPacket
{
    public readonly MessageID Type => MessageID.TileCounts;
    public byte Good;
    public byte Evil;
    public byte Blood;
}