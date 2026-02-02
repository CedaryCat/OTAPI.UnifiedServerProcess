using Microsoft.Xna.Framework;

namespace TrProtocol.NetPackets;

public partial struct SpecialFX : INetPacket
{
    public readonly MessageID Type => MessageID.SpecialFX;
    public byte GrowType;
    public Point Position;
    public byte Height;
    public short Gore;
    public bool HitTree;
}