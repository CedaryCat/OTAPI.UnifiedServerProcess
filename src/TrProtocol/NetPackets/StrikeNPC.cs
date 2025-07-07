using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct StrikeNPC : INetPacket, INPCSlot
{
    public readonly MessageID Type => MessageID.StrikeNPC;
    public short NPCSlot { get; set; }
    public short Damage;
    public float Knockback;
    public byte HitDirection;
    public bool Crit;
}