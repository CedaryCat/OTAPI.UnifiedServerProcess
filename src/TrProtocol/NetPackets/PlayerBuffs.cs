using TrProtocol.Attributes;
using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct PlayerBuffs : INetPacket, IPlayerSlot
{
    public readonly MessageID Type => MessageID.PlayerBuffs;
    public byte PlayerSlot { get; set; }

    [TerminatedArray(44)]
    public ushort[] BuffTypes;
}