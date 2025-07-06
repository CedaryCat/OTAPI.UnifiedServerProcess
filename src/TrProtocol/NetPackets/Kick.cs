using Terraria.Localization;

namespace TrProtocol.NetPackets;

public partial struct Kick : INetPacket {
    public readonly MessageID Type => MessageID.Kick;
    public NetworkText Reason;
}
