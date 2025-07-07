using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TeleportPlayerThroughPortal : INetPacket, IOtherPlayerSlot
{
    public readonly MessageID Type => MessageID.TeleportPlayerThroughPortal;
    public byte OtherPlayerSlot { get; set; }
    public ushort Extra;
    public Vector2 Position;
    public Vector2 Velocity;
}