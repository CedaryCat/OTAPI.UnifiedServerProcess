namespace TrProtocol.NetPackets;

public partial struct TeleportationPotion : INetPacket
{
    public readonly MessageID Type => MessageID.TeleportationPotion;
    public byte Style;
}