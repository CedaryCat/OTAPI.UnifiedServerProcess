using Microsoft.Xna.Framework;

namespace TrProtocol.Models.Interfaces;


public interface IItemBase : IItemSlot
{
    Vector2 Position { get; set; }
    Vector2 Velocity { get; set; }
    short Stack { get; set; }
    byte Prefix { get; set; }
    byte Owner { get; set; }
    short ItemType { get; set; }
}
