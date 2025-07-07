using Terraria.DataStructures;

namespace TrProtocol.NetPackets;

public partial struct GemLockToggle : INetPacket
{
    public readonly MessageID Type => MessageID.GemLockToggle;
    public Point16 Position;
    public bool Flag;
}