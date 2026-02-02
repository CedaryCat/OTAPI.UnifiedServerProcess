namespace TrProtocol.NetPackets;

public partial struct NPCDebuffDamage : INetPacket
{
    public readonly MessageID Type => MessageID.NPCDebuffDamage;
    public byte NPCIndex;
    public short Damage;
}
