namespace TrProtocol.NetPackets;

public partial struct InvasionProgressReport : INetPacket
{
    public readonly MessageID Type => MessageID.InvasionProgressReport;
    public int Progress;
    public int ProgressMax;
    public sbyte Icon;
    public sbyte Wave;
}