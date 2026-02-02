using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct RequestTileData : INetPacket
{
    public readonly MessageID Type => MessageID.RequestTileData;
    public Point Position;
    public byte Team;
}
