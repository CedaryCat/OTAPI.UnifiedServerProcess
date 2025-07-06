using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct AddNPCBuff : INetPacket, INPCSlot {
    public readonly MessageID Type => MessageID.AddNPCBuff;
    public short NPCSlot { get; set; }
    public ushort BuffType;
    public short BuffTime;
}
