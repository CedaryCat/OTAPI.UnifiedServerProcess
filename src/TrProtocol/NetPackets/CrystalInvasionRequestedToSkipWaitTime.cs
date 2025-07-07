namespace TrProtocol.NetPackets;

public partial struct CrystalInvasionRequestedToSkipWaitTime : INetPacket
{
    public readonly MessageID Type => MessageID.CrystalInvasionRequestedToSkipWaitTime;
}