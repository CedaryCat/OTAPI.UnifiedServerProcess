namespace TrProtocol.NetPackets;

public partial struct MoonlordCountdown : INetPacket {
    public readonly MessageID Type => MessageID.MoonlordCountdown;
    public int MaxCountdown;
    public int Countdown;
}