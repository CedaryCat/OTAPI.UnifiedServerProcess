using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace TrProtocol.NetPackets;

public partial struct CombatTextString : INetPacket
{
    public readonly MessageID Type => MessageID.CombatTextString;
    public Vector2 Position;
    public Color Color;
    public NetworkText Text;
}