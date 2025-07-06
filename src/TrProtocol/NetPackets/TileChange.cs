using TrProtocol.Models;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct TileChange : INetPacket {
    public readonly MessageID Type => MessageID.TileChange;
    public TileEditAction ChangeType;
    public Point16 Position;
    public short TileType;
    public byte Style;
}
