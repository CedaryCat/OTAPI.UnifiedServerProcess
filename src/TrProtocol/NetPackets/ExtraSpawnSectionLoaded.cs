using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ExtraSpawnSectionLoaded : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.ExtraSpawnSectionLoaded;
    public byte PlayerSlot { get; set; }
}
