using Terraria.Localization;

namespace TrProtocol.NetPackets;

public partial struct StatusText : INetPacket {
    public readonly MessageID Type => MessageID.StatusText;
    public int Max;
    public NetworkText Text;
    public byte Flag;
}
