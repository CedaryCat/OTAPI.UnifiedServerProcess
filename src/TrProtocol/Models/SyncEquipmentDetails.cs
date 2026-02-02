using Terraria;
using TrProtocol.Interfaces;

namespace TrProtocol.Models;

public struct SyncEquipmentDetails : IPackedSerializable
{
    public BitsByte packedValue;
    public bool Favorited {
        get => packedValue[0];
        set => packedValue[0] = value;
    }
    public bool IndicateBlockedSlot {
        get => packedValue[1];
        set => packedValue[1] = value;
    }
}
