namespace TrProtocol.NetPackets;

public partial struct AchievementMessageEventHappened : INetPacket
{
    public readonly MessageID Type => MessageID.AchievementMessageEventHappened;
    public short EventType;
}
