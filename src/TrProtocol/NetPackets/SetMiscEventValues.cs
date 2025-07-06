using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SetMiscEventValues : INetPacket, IOtherPlayerSlot {
    public readonly MessageID Type => MessageID.SetMiscEventValues;
    public byte OtherPlayerSlot { get; set; }
    public int CreditsRollTime;
}