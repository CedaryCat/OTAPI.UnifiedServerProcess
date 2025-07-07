using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct PaintTile : INetPacket
{
    public readonly MessageID Type => MessageID.PaintTile;
    public Point16 Position;
    public byte Color;
}