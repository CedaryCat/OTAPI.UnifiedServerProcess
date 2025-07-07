using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct WeaponsRackTryPlacing : INetPacket
{
    public readonly MessageID Type => MessageID.WeaponsRackTryPlacing;
    public Point16 Position;
    public short ItemType;
    public byte Prefix;
    public short Stack;
}