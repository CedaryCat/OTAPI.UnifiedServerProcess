using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct AnglerQuestCountSync : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.AnglerQuestCountSync;
    public byte PlayerSlot { get; set; }
    public int AnglerQuestsFinished;
    public int GolferScoreAccumulated;
}
