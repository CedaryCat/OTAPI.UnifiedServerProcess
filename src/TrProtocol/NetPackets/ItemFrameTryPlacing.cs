using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct ItemFrameTryPlacing : INetPacket
{
    public readonly MessageID Type => MessageID.ItemFrameTryPlacing;
    public Point16 Position;
    public short ItemType;
    public byte Prefix;
    public short Stack;
}