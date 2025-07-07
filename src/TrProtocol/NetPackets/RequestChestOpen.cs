using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct RequestChestOpen : INetPacket
{
    public readonly MessageID Type => MessageID.RequestChestOpen;
    public Point16 Position;
}