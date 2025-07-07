using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TeleportNPCThroughPortal : INetPacket, INPCSlot
{
    public readonly MessageID Type => MessageID.TeleportNPCThroughPortal;
    public short NPCSlot { get; set; }
    public ushort Extra;
    public Vector2 Position;
    public Vector2 Velocity;
}