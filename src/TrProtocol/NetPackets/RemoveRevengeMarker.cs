namespace TrProtocol.NetPackets;

public partial struct RemoveRevengeMarker : INetPacket {
    public readonly MessageID Type => MessageID.RemoveRevengeMarker;
    public int ID;
}