namespace TrProtocol.NetPackets
{

    public partial struct AnglerQuest : INetPacket
    {
        public readonly MessageID Type => MessageID.AnglerQuest;
        public byte QuestType;
        public bool Finished;
    }
}
