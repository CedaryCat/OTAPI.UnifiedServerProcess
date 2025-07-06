namespace TrProtocol.NetPackets;

public partial struct ToggleParty : INetPacket {
    public readonly MessageID Type => MessageID.ToggleParty;
}