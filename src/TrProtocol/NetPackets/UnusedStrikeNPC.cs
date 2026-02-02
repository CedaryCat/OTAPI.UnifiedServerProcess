using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct UnusedStrikeNPC : INetPacket, INPCSlot, IPlayerSlot
{
    public readonly MessageID Type => MessageID.UnusedStrikeNPC;

    public short NPCSlot { get; set; }
    public byte PlayerSlot { get; set; }
}
