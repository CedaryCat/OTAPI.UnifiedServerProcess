using Microsoft.Xna.Framework;
using static Terraria.GameContent.LucyAxeMessage;

namespace TrProtocol.NetPackets;

public partial struct RequestLucyPopup : INetPacket
{
    public readonly MessageID Type => MessageID.RequestLucyPopup;
    public MessageSource Source;
    public byte Variation;
    public Vector2 Velocity;
    public Point Position;
}