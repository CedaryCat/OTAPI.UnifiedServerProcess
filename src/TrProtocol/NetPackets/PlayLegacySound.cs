using TrProtocol.Attributes;
using Microsoft.Xna.Framework;
using Terraria;

namespace TrProtocol.NetPackets;

public partial struct PlayLegacySound : INetPacket {
    public readonly MessageID Type => MessageID.PlayLegacySound;
    public Vector2 Point;
    public ushort Sound;
    public BitsByte Bits1;
    [Condition(nameof(Bits1), 0)]
    public int Style;
    [Condition(nameof(Bits1), 1)]
    public float Volume;
    [Condition(nameof(Bits1), 2)]
    public float Pitch;
}