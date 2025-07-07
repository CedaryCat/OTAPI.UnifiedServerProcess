using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SyncItem : INetPacket, IItemSlot
{
    public readonly MessageID Type => MessageID.SyncItem;
    public short ItemSlot { get; set; }
    public Vector2 Position;
    public Vector2 Velocity;
    public short Stack;
    public byte Prefix;
    public byte Owner;
    public short ItemType;
}
