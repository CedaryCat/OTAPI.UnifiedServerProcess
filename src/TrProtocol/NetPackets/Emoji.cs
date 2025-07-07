using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct Emoji : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.Emoji;
    public byte PlayerSlot { get; set; }
    public byte Emote;
}