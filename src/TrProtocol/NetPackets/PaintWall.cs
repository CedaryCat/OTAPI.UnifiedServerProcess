using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct PaintWall : INetPacket
{
    public readonly MessageID Type => MessageID.PaintWall;
    public Point16 Position;
    public byte Color;
}