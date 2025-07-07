
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncNPCName : INetPacket, INPCSlot, ISideSpecific
{
    public readonly MessageID Type => MessageID.SyncNPCName;
    public short NPCSlot { get; set; }
    [S2COnly]
    public string? NPCName;
    [S2COnly]
    public int TownNpc;
}
