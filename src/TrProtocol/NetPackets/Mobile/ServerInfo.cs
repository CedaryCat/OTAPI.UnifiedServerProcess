namespace TrProtocol.NetPackets.Mobile;

public partial struct ServerInfo : INetPacket
{
    public readonly MessageID Type => MessageID.ServerInfo;
    public int ListenPort;
    public string WorldName;
    public string HostName;
    public int MaxTilesX;
    public bool IsCrimson;
    public byte GameMode;
    public bool UnknownBool1;
    public bool UnknownBool2;
    public bool UnknownBool3;
    public byte MaxNetPlayers;
    public byte NumberOfPlayers;
}
