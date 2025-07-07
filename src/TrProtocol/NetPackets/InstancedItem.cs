using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct InstancedItem : INetPacket, IItemSlot
{
    public readonly MessageID Type => MessageID.InstancedItem;
    public short ItemSlot { get; set; }
    public Vector2 Position;
    public Vector2 Velocity;
    public short Stack;
    public byte Prefix;
    public byte Owner;
    public short ItemType;
}
