namespace TrProtocol.NetPackets;

public partial struct MenuSunMoon : INetPacket
{
    public readonly MessageID Type => MessageID.MenuSunMoon;
    public bool DayTime;
    public int Time;
    public short Sun;
    public short Moon;
}
