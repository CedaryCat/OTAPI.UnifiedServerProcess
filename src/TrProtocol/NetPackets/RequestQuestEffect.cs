namespace TrProtocol.NetPackets;

public partial struct RequestQuestEffect : INetPacket
{
    public readonly MessageID Type => MessageID.RequestQuestEffect;
}