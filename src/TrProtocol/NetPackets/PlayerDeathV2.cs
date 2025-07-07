using Terraria;
using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerDeathV2 : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerDeathV2;
    public byte PlayerSlot { get; set; }
    public PlayerDeathReason Reason;
    public short Damage;
    public byte HitDirection;
    public BitsByte Bits1;
}