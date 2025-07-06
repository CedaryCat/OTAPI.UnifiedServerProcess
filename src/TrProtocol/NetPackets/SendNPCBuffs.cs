using TrProtocol.Attributes;
using TrProtocol.Models;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SendNPCBuffs : INetPacket, INPCSlot {
    public readonly MessageID Type => MessageID.SendNPCBuffs;
    public short NPCSlot { get; set; }
    [ArraySize(20)]
    public Buff[] Buffs;
}