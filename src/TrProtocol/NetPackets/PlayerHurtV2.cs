using Terraria;
using Terraria.DataStructures;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerHurtV2 : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerHurtV2;
    public byte OtherPlayerSlot { get; set; }
    public PlayerDeathReason Reason;
    public short Damage;
    public byte HitDirection;
    public BitsByte Bits1;
    public sbyte CoolDown;
}