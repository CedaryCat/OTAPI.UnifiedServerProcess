using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using static Terraria.Recipe;

namespace TrProtocol.NetPackets.Mobile;

public partial struct NetCraftingRequest : IAutoSerializable
{
    [Int7BitEncoded]
    public int RequiredItemsCount;
    [ArraySize(nameof(RequiredItemsCount))]
    public RequiredItemEntry[] RequiredItems;

    [Int7BitEncoded]
    public int NearbyChestCount;
    [Int7BitEncoded]
    [ArraySize(nameof(NearbyChestCount))]
    public int[] NearbyChestIndexes;
}