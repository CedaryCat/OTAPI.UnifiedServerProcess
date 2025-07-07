using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncProjectileTrackers : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncProjectileTrackers;
    public byte PlayerSlot { get; set; }

    public short ExpectedOwner1;
    [ConditionNotEqual(nameof(ExpectedOwner1), -1)]
    public short ExpectedIdentity1;
    [ConditionNotEqual(nameof(ExpectedOwner1), -1)]
    public short ExpectedType1;

    public short ExpectedOwner2;
    [ConditionNotEqual(nameof(ExpectedOwner2), -1)]
    public short ExpectedIdentity2;
    [ConditionNotEqual(nameof(ExpectedOwner2), -1)]
    public short ExpectedType2;
}