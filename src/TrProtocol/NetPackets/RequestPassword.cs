namespace TrProtocol.NetPackets;

public partial struct RequestPassword : INetPacket {
    public readonly MessageID Type => MessageID.RequestPassword;
}
