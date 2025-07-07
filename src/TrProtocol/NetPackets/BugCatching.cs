using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct BugCatching : INetPacket, IPlayerSlot, INPCSlot
{
    public readonly MessageID Type => MessageID.BugCatching;
    public short NPCSlot { get; set; }
    public byte PlayerSlot { get; set; }
}