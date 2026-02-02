namespace TrProtocol.NetPackets;

public partial struct DeadCellsDisplayJarTryPlacing : INetPacket
{
    public readonly MessageID Type => MessageID.DeadCellsDisplayJarTryPlacing;
    public short X;
    public short Y;
    public short ItemType;
    public byte Prefix;
    public short Stack;
}
