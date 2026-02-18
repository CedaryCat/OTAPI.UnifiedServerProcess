namespace TrProtocol.NetPackets;

public partial struct DevCommands : INetPacket
{
    public readonly MessageID Type => MessageID.DevCommands;

    public string Command;
    public int Arg1;
    public float Arg2;
    public float Arg3;
}
