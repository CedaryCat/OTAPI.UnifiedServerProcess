namespace TrProtocol.NetPackets;

public partial struct PoofOfSmoke : INetPacket
{
    public readonly MessageID Type => MessageID.PoofOfSmoke;
    public uint PackedHalfVector2;
}