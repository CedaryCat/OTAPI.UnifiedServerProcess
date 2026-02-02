namespace TrProtocol.NetPackets;

public partial struct HostToken : INetPacket
{
    public readonly MessageID Type => MessageID.HostToken;
    public string Token;
}
