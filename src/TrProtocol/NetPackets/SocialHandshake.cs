using TrProtocol.Interfaces;

namespace TrProtocol.NetPackets;

public partial struct SocialHandshake : INetPacket, IExtraData {
    public readonly MessageID Type => MessageID.SocialHandshake;
}