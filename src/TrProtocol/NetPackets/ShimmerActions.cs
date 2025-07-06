using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;
using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct ShimmerActions : INetPacket, INPCSlot {
    public readonly MessageID Type => MessageID.ShimmerActions;
    public byte ShimmerType;
    [ConditionEqual(nameof(ShimmerType), 0)]
    public Vector2 ShimmerPosition;
    [ConditionEqual(nameof(ShimmerType), 1)]
    public Vector2 CoinPosition;
    [ConditionEqual(nameof(ShimmerType), 1)]
    public int CoinAmount;
    [ConditionEqual(nameof(ShimmerType), 2)]
    public short NPCSlot { get; set; }
    [ConditionEqual(nameof(ShimmerType), 2)]
    public short NPCSlotHighBits;
}