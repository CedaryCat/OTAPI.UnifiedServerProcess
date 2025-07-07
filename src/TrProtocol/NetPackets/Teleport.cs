using Microsoft.Xna.Framework;
using Terraria;
using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct Teleport : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.Teleport;
    public BitsByte Bit1;
    public byte PlayerSlot { get; set; }
    [InitNullable]
    public byte HighBitOfPlayerIsAlwaysZero = 0;
    public Vector2 Position;
    public byte Style;
    [Condition(nameof(Bit1), 3)]
    public int ExtraInfo;
}