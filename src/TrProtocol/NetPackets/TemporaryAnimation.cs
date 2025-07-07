using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct TemporaryAnimation : INetPacket
{
    public readonly MessageID Type => MessageID.TemporaryAnimation;
    public short AniType;
    public short TileType;
    public Point16 Position;
}