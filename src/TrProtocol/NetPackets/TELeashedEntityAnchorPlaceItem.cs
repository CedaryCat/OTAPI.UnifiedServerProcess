namespace TrProtocol.NetPackets;

public partial struct TELeashedEntityAnchorPlaceItem : INetPacket
{
    public readonly MessageID Type => MessageID.TELeashedEntityAnchorPlaceItem;
    public short X;
    public short Y;
    public short ItemType;
}
