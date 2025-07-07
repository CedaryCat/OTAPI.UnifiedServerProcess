using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerTalkingNPC : INetPacket, IPlayerSlot, INPCSlot
{
    public readonly MessageID Type => MessageID.PlayerTalkingNPC;
    public byte PlayerSlot { get; set; }
    public short NPCSlot { get; set; }
}