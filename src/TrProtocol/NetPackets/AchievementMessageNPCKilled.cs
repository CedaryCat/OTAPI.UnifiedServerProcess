namespace TrProtocol.NetPackets;

public partial struct AchievementMessageNPCKilled : INetPacket
{
    public readonly MessageID Type => MessageID.AchievementMessageNPCKilled;
    public short NPCType;
}
