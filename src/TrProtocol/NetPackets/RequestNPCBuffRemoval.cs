using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct RequestNPCBuffRemoval : INetPacket, INPCSlot {
    public readonly MessageID Type => MessageID.RequestNPCBuffRemoval;
    public short NPCSlot { get; set; }
    public ushort BuffType;
}