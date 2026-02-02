using Microsoft.Xna.Framework;
using Terraria;
using TrProtocol.Models.Interfaces;
namespace TrProtocol.NetPackets;

public partial struct SyncPlayer : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.SyncPlayer;
    public byte PlayerSlot { get; set; }
    public byte SkinVariant;
    public byte VoiceVariant;
    public float VoicePitchOffset;
    public byte Hair;
    public string Name;
    public byte HairDye;
    public BitsByte Bit1;
    public BitsByte Bit2;
    public byte HideMisc;
    public Color HairColor;
    public Color SkinColor;
    public Color EyeColor;
    public Color ShirtColor;
    public Color UnderShirtColor;
    public Color PantsColor;
    public Color ShoeColor;
    public BitsByte Bit3;
    public BitsByte Bit4;
    public BitsByte Bit5;
}
