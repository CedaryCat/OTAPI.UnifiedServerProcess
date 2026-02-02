using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct ItemUseSound : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.ItemUseSound;
    public byte PlayerSlot { get; set; }
}
