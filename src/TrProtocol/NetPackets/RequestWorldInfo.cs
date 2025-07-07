namespace TrProtocol.NetPackets;

public partial struct RequestWorldInfo : INetPacket
{
    public readonly MessageID Type => MessageID.RequestWorldInfo;
}
