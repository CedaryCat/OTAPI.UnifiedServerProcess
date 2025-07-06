using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TamperWithNPC : INetPacket, INPCSlot, IOtherPlayerSlot {
    public readonly MessageID Type => MessageID.TamperWithNPC;
    public short NPCSlot { get; set; }
    public byte UniqueImmune;

    [ConditionEqual(nameof(UniqueImmune), 1)]
    public int Time;
    [ConditionEqual(nameof(UniqueImmune), 1)]
    public byte OtherPlayerSlot { get; set; }
    public byte HighBitOfPlayerIsAlwaysZero = 0;
}