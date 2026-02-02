namespace TrProtocol.NetPackets;

public partial struct ClientHello : INetPacket
{
    public readonly MessageID Type => MessageID.ClientHello;
    public string Version;
}
