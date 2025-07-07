using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct RequestReadSign : INetPacket
{
    public readonly MessageID Type => MessageID.RequestReadSign;
    public Point16 Position;
}