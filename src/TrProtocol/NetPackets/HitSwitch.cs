using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct HitSwitch : INetPacket
{
    public readonly MessageID Type => MessageID.HitSwitch;
    public Point16 Position;
}