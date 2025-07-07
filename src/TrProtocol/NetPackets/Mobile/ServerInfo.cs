namespace TrProtocol.NetPackets.Mobile
{
    public partial struct ServerInfo : INetPacket
    {
        public readonly MessageID Type => MessageID.ServerInfo;
        public int ListenPort;
        public string WorldName;
        public int MaxTilesX;
        public bool IsCrimson;
        public byte GameMode;
        public byte maxNetPlayers;
    }
}
