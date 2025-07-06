using TrProtocol.Models.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct LoadPlayer : INetPacket, IPlayerSlot {
    public readonly MessageID Type => MessageID.LoadPlayer;
    public byte PlayerSlot { get; set; }
    public bool ServerWantsToRunCheckBytesInClientLoopThread;
}
