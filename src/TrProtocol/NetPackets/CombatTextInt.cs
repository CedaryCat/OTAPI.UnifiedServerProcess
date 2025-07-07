using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct CombatTextInt : INetPacket
{
    public readonly MessageID Type => MessageID.CombatTextInt;
    public Vector2 Position;
    public Color Color;
    public int Amount;
}