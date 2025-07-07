using System.IO.Compression;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models.TileEntities;

namespace TrProtocol.Models;

[Compress(CompressionLevel.SmallestSize, 1024 * 128)]
public partial struct SectionData : IAutoSerializable, ILengthAware
{
    public int StartX;
    public int StartY;
    public short Width;
    public short Height;

    public int ComplexTileCount => Width * Height;

    [ArraySize(nameof(ComplexTileCount))]
    public ComplexTileData[] Tiles;

    public short ChestCount;
    [ArraySize(nameof(ChestCount))]
    public ChestData[] Chests;

    public short SignCount;
    [ArraySize(nameof(SignCount))]
    public SignData[] Signs;

    public short TileEntityCount;
    [ArraySize(nameof(TileEntityCount))]
    [ExternalMemberValue(nameof(TileEntity.NetworkSend), false)]
    public TileEntity[] TileEntities;
}
