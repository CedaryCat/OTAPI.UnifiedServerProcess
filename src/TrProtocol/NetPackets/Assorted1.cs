using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct Assorted1 : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.Assorted1;
    public byte PlayerSlot { get; set; }
    public byte Unknown;
}