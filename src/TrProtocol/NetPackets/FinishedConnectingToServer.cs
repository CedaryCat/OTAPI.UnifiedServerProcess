namespace TrProtocol.NetPackets;

public partial struct FinishedConnectingToServer : INetPacket {
    public readonly MessageID Type => MessageID.FinishedConnectingToServer;
}