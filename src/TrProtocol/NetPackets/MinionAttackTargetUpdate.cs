using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct MinionAttackTargetUpdate : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.MinionAttackTargetUpdate;
    public byte PlayerSlot { get; set; }
    public short MinionAttackTarget;
}