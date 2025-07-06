namespace TrProtocol.NetPackets {

    public partial struct AnglerQuestFinished : INetPacket {
        public readonly MessageID Type => MessageID.AnglerQuestFinished;
    }
}
