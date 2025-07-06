using TrProtocol.Models.Interfaces;
using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct WiredCannonShot : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.WiredCannonShot;
    public short Damage;
    public float Knockback;
    public Point16 Position;
    public short Angle;
    public short Ammo;
    public byte PlayerSlot { get; set; }
}