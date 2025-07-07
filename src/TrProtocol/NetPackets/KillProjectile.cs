using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct KillProjectile : INetPacket, IProjSlot, IPlayerSlot
{
    public readonly MessageID Type => MessageID.KillProjectile;
    public short ProjSlot { get; set; }
    public byte PlayerSlot { get; set; }
}