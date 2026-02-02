using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct TeamChangeFromUI : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.TeamChangeFromUI;
    public byte PlayerSlot { get; set; }
    public byte Team;
}
