using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct TileEntityPlacement : INetPacket
{
    public readonly MessageID Type => MessageID.TileEntityPlacement;
    public Point16 Position;
    public byte TileEntityType;
}