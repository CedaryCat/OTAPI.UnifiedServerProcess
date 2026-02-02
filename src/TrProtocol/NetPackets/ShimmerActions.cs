using Microsoft.Xna.Framework;
using TrProtocol.Attributes;

namespace TrProtocol.NetPackets;

public partial struct ShimmerActions : INetPacket
{
    public readonly MessageID Type => MessageID.ShimmerActions;
    public byte ShimmerType;
    [ConditionEqual(nameof(ShimmerType), 0)]
    public Vector2 ShimmerPosition;
    [ConditionEqual(nameof(ShimmerType), 1)]
    public Vector2 CoinPosition;
    [ConditionEqual(nameof(ShimmerType), 1)]
    public int CoinAmount;
}
