using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerTeam : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerTeam;
    public byte PlayerSlot { get; set; }
    public byte Team;
}