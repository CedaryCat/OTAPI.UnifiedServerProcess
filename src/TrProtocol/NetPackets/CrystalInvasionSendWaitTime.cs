namespace TrProtocol.NetPackets;

public partial struct CrystalInvasionSendWaitTime : INetPacket {
    public readonly MessageID Type => MessageID.CrystalInvasionSendWaitTime;
    public int WaitTime;
}