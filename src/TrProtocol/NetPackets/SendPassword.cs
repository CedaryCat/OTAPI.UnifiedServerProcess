namespace TrProtocol.NetPackets;

public partial struct SendPassword : INetPacket
{
    public readonly MessageID Type => MessageID.SendPassword;
    public string Password;
}
