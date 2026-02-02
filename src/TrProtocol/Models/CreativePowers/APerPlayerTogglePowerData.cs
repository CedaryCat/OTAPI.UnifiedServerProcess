using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using static Terraria.GameContent.Creative.CreativePowers.APerPlayerTogglePower;

namespace TrProtocol.Models.CreativePowers;

public partial struct APerPlayerTogglePowerData : IAutoSerializable
{
    public SubMessageType SubMessageType;
    [ConditionEqual(nameof(SubMessageType), SubMessageType.SyncEveryone)]
    public BitsArray256 PerPlayerIsEnabled;

    [ConditionEqual(nameof(SubMessageType), SubMessageType.SyncOnePlayer)]
    public byte PlayerSlot;
    [ConditionEqual(nameof(SubMessageType), SubMessageType.SyncOnePlayer)]
    public bool EnableState;
}