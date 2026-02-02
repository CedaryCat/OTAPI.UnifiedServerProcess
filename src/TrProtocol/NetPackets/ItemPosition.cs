using Microsoft.Xna.Framework;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ItemPosition : INetPacket, IItemSlot
{
    public readonly MessageID Type => MessageID.ItemPosition;
    public short ItemSlot { get; set; }
    public Vector2 Position;
}
