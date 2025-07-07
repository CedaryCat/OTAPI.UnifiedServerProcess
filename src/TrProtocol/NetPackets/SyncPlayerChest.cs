using Terraria.DataStructures;
using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct SyncPlayerChest : INetPacket
{
    public readonly MessageID Type => MessageID.SyncPlayerChest;
    public short Chest;
    public Point16 Position;
    public byte NameLength;
    [ConditionGreaterThan(nameof(NameLength), 0), ConditionLessThanEqual(nameof(NameLength), 20)]
    public string? Name;
}