using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerMana : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerMana;
    public byte PlayerSlot { get; set; }
    public short StatMana;
    public short StatManaMax;
}