using TrProtocol.Models;

namespace TrProtocol.NetPackets;

public partial struct TileSquare : INetPacket {
    public readonly MessageID Type => MessageID.TileSquare;
    public SquareData Data;
}
