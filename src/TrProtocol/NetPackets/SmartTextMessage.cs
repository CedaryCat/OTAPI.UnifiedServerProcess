using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace TrProtocol.NetPackets;

public partial struct SmartTextMessage : INetPacket
{
    public readonly MessageID Type => MessageID.SmartTextMessage;
    public Color Color;
    public NetworkText Text;
    public short Width;
}
