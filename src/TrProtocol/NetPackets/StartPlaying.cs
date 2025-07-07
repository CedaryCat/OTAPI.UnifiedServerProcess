namespace TrProtocol.NetPackets;

public partial struct StartPlaying : INetPacket
{
    public readonly MessageID Type => MessageID.StartPlaying;
}
