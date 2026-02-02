namespace TrProtocol.NetPackets;

public partial struct Ping : INetPacket
{
    public readonly MessageID Type => MessageID.Ping;
}
