namespace TrProtocol.NetPackets;

public partial struct RequestSection : INetPacket
{
    public readonly MessageID Type => MessageID.RequestSection;
    public ushort SectionX;
    public ushort SectionY;
}
