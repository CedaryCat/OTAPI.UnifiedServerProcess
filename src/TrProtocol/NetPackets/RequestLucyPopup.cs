using Microsoft.Xna.Framework;
using Terraria.GameContent;

namespace TrProtocol.NetPackets;

public partial struct RequestLucyPopup : INetPacket
{
    public readonly MessageID Type => MessageID.RequestLucyPopup;
    public LucyAxeMessage_MessageSource Source;
    public byte Variation;
    public Vector2 Velocity;
    public Point Position;
}