namespace TrProtocol.NetPackets;

public partial struct ClientUUID : INetPacket
{
    public readonly MessageID Type => MessageID.ClientUUID;
    public string UUID;
}