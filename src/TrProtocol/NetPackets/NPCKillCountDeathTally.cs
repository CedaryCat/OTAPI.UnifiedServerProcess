namespace TrProtocol.NetPackets;

public partial struct NPCKillCountDeathTally : INetPacket
{
    public readonly MessageID Type => MessageID.NPCKillCountDeathTally;
    public short NPCType;
    public int Count;
}