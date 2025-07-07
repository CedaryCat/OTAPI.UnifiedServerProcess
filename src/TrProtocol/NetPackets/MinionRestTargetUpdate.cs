using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct MinionRestTargetUpdate : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.MinionRestTargetUpdate;
    public byte PlayerSlot { get; set; }
    public Vector2 MinionRestTargetPoint;
}