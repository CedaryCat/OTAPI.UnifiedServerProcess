using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;
public partial struct SquareData : IAutoSerializable {
    public short TilePosX;
    public short TilePosY;
    public byte Width;
    public byte Height;
    public TileChangeType ChangeType;
    [ArraySize(nameof(Width), nameof(Height))]
    public SimpleTileData[,] Tiles;
}
