using TrProtocol.Attributes;
using TrProtocol.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct QuickStackChests : INetPacket, ISideSpecific
{
    public readonly MessageID Type => MessageID.QuickStackChests;
    [C2SOnly]
    public QuickStackRequestC2S QuickStackRequest;
    [S2COnly]
    public IndicateBlockedChestsS2C IndicateBlockedChests;
}
public struct QuickStackRequestC2S
{
    public int SlotsCount;
    [ArraySize(nameof(SlotsCount))]
    public short[] ReferenceInventorySlots;
    public bool SmartStack;
}
public struct IndicateBlockedChestsS2C {
    public int BlockedChestsCount;
    [ArraySize(nameof(BlockedChestsCount))]
    public ushort[] BlockedChests;
}